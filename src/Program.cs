using Microsoft.DiaSymReader;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;

internal class Program
{

    // https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#source-link-c-and-vb-compilers
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

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

    static BlobReader GetCustomDebugInformationReader(MetadataReader metadataReader, EntityHandle handle, Guid kind)
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
