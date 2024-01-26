using Microsoft.DiaSymReader;
using Microsoft.SourceLink.Tools;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SourceLinkCheck;

internal class Program
{

    // See https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#source-link-c-and-vb-compilers
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    // See https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#embedded-source-c-and-vb-compilers
    private static readonly Guid EmbeddedSourceKind = Guid.Parse("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    // See https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#document-table-0x30
    private static readonly (Guid guid, HashAlgorithmName algorithm)[] KnownHashAlgorithms = new[]
    {
        (new Guid("406ea660-64cf-4c82-b6f0-42d48172a799"), HashAlgorithmName.MD5),
        (new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460"), HashAlgorithmName.SHA1),
        (new Guid("8829d00f-11b8-4213-878b-770e8597ac16"), HashAlgorithmName.SHA256)
    };

    private static readonly Guid CSharpLanguage = new("3f5162f8-07c6-11d3-9053-00c04fa302a1");

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: SourceLinkCheck <pdbfile>");
            Console.WriteLine();
            Console.WriteLine("Missing input file.");
            return -1;
        }

        string path = args[0];
        FileStream inputStream;
        try
        {
            inputStream = File.OpenRead(path);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("Cannot open the given file.");
            return 404;
        }

        try
        {
            bool sourceLinkFound;
            try
            {
                sourceLinkFound = DumpPortablePdb(inputStream);
            }
            catch (BadImageFormatException)
            {
                // Probably not a portable PDB
                sourceLinkFound = DumpWindowsPdb(inputStream);
            }

            if (!sourceLinkFound)
            {
                Console.WriteLine("No source link information found.");
                return 404;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Exception: {0}", ex);
            return -1;
        }
    }

    private static bool DumpPortablePdb(FileStream inputStream)
    {
        MetadataReaderProvider pdbReaderProvider = MetadataReaderProvider.FromPortablePdbStream(inputStream, MetadataStreamOptions.LeaveOpen);
        MetadataReader reader = pdbReaderProvider!.GetMetadataReader();
        BlobReader sourceLinkReader = GetCustomDebugInformationReader(reader, EntityHandle.ModuleDefinition, SourceLinkKind);
        if (sourceLinkReader.Length == 0)
        {
            return false;
        }

        // Dump the source link JSON
        string json = sourceLinkReader.ReadUTF8(sourceLinkReader.Length);

        Console.WriteLine(json);

        SourceLinkMap sourceLinkMap = SourceLinkMap.Parse(json);

        Dictionary<GuidHandle, string> hashAlgorithmMap = new();
        GuidHandle csharpGuidHandle = default;
        foreach (DocumentHandle documentHandle in reader.Documents)
        {
            Document doc = reader.GetDocument(documentHandle);

            DocumentNameBlobHandle nameHandle = doc.Name;
            string name = reader.GetString(nameHandle);

            GuidHandle languageHandle = doc.Language;
            if (csharpGuidHandle == default)
            {
                Guid language = reader.GetGuid(languageHandle);
                if (language == CSharpLanguage)
                {
                    csharpGuidHandle = languageHandle;
                }
                else
                {
                    // Not C#
                    continue;
                }
            }
            else if (languageHandle != csharpGuidHandle)
            {
                // Not C#
                continue;
            }

            GuidHandle algorithmHandle = doc.HashAlgorithm;
            if (!hashAlgorithmMap.TryGetValue(algorithmHandle, out string? hashAlgorithmName))
            {
                Guid algorithm = reader.GetGuid(algorithmHandle);
                foreach (var knownHashAlgorithm in KnownHashAlgorithms)
                {
                    if (algorithm == knownHashAlgorithm.guid)
                    {
                        hashAlgorithmName = knownHashAlgorithm.algorithm.Name;
                        break;
                    }
                }

                hashAlgorithmMap[algorithmHandle] = hashAlgorithmName ??= "Unknown";
            }

            BlobHandle hashHandle = doc.Hash;
            byte[] hash = reader.GetBlobBytes(hashHandle);

            // Is it embedded source?
            BlobReader embeddedSourceReader = GetCustomDebugInformationReader(reader, documentHandle, EmbeddedSourceKind);
            if (embeddedSourceReader.Length != 0)
            {
                Console.WriteLine("Embedded {0} {1} {2}", name, hashAlgorithmName, string.Join("", hash.Select(b => b.ToString("x2"))));

                // Skip the first 4 bytes (the uncompressed length)
                int uncompressedSize = embeddedSourceReader.ReadInt32();

                Stream? embeddedSourceStream = null;
                try
                {
                    unsafe
                    {
                        if (uncompressedSize == 0)
                        {
                            embeddedSourceStream = new UnmanagedMemoryStream(embeddedSourceReader.CurrentPointer, embeddedSourceReader.RemainingBytes);
                        }
                        else
                        {
                            embeddedSourceStream = new MemoryStream(uncompressedSize);
                            using var source = new UnmanagedMemoryStream(embeddedSourceReader.CurrentPointer, embeddedSourceReader.RemainingBytes);
                            using var deflater = new DeflateStream(source, CompressionMode.Decompress);
                            deflater.CopyTo(embeddedSourceStream);

                            embeddedSourceStream.Position = 0;
                        }
                    }

                    using StreamReader streamReader = new(embeddedSourceStream, detectEncodingFromByteOrderMarks: true);
                    string text = streamReader.ReadToEnd();
                    Console.WriteLine(text);
                }
                finally
                {
                    embeddedSourceStream?.Dispose();
                }
            }
            else if (sourceLinkMap.TryGetUri(name, out string? uri))
            {
                Console.WriteLine("{0} {1} {2}", uri, hashAlgorithmName, string.Join("", hash.Select(b => b.ToString("x2"))));

                // HttpClient ... 
            }
            else
            {
                Console.WriteLine("{0} {1} {2}", name, hashAlgorithmName, string.Join("", hash.Select(b => b.ToString("x2"))));
            }
        }

        // Success
        return true;
    }

    private static unsafe bool DumpWindowsPdb(FileStream inputStream)
    {
        ISymUnmanagedReader4 reader = SymUnmanagedReaderFactory.CreateReader<ISymUnmanagedReader4>(inputStream, DummySymReaderMetadataProvider.Instance);
        int hr = reader.GetSourceServerData(out byte* data, out int size);
        Marshal.ThrowExceptionForHR(hr);
        if (hr == 0)
        {
            string json = Encoding.UTF8.GetString(data, size);
            Console.WriteLine(json);
            return true;
        }

        return false;
    }

    private static BlobReader GetCustomDebugInformationReader(MetadataReader metadataReader, EntityHandle handle, Guid kind)
    {
        foreach (CustomDebugInformationHandle cdiHandle in metadataReader.GetCustomDebugInformation(handle))
        {
            CustomDebugInformation cdi = metadataReader.GetCustomDebugInformation(cdiHandle);
            if (metadataReader.GetGuid(cdi.Kind) == kind)
            {
                return metadataReader.GetBlobReader(cdi.Value);
            }
        }

        return default;
    }
}
