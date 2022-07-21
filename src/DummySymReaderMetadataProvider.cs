using Microsoft.DiaSymReader;
using System.Reflection;

public sealed class DummySymReaderMetadataProvider : ISymReaderMetadataProvider
{
    public static readonly ISymReaderMetadataProvider Instance = new DummySymReaderMetadataProvider();

    public unsafe bool TryGetStandaloneSignature(int standaloneSignatureToken, out byte* signature, out int length)
        => throw new NotSupportedException();

    public bool TryGetTypeDefinitionInfo(int typeDefinitionToken, out string namespaceName, out string typeName, out TypeAttributes attributes)
        => throw new NotSupportedException();

    public bool TryGetTypeReferenceInfo(int typeReferenceToken, out string namespaceName, out string typeName)
        => throw new NotSupportedException();
}
