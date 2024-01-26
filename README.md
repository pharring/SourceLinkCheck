# SourceLinkCheck
A command line tool to extract [SourceLink](https://aka.ms/sourcelink) information from a [Program Database (PDB)](https://wikipedia.org/wiki/Program_database) file.

## Usage
`SourceLinkCheck <pdbfile>`

Where `pdbfile` is a path to a symbol file -- usually with a .pdb extension. The symbol file may be either a Windows PDB or a [Portable PDB](https://learn.microsoft.com/windows-hardware/drivers/debugger/symbols-portable-pdb).
If SourceLink information is present in the PDB, the tool will print out the contents of the source link manifest.

### Example
- Running SourceLinkCheck on its own PDB produces:
  ```
  >SourceLinkCheck SourceLinkCheck.pdb
  {"documents":{"C:\\.tools\\.nuget\\packages\\microsoft.sourcelink.tools\\1.1.1\\contentFiles\\cs\\netstandard2.0\\*":"https://raw.githubusercontent.com/dotnet/sourcelink/3539d92ff8bd5ddc3277f94bf3d1e04818c3d0ab/src/SourceLink.Tools/*","C:\\github\\pharring\\sourcelinkcheck\\*":"https://raw.githubusercontent.com/pharring/sourcelinkcheck/de62087fac77f813c26419e389499719d73dc033/*"}}
  ```

- Running SourceLinkCheck on a PDB without SourceLink information prints:
  ```
  No source link information found.
  ```

## Building
- Clone the repo locally: `git clone https://github.com/pharring/SourceLinkCheck.git`
- `cd src`
- `dotnet build`

The tool (SourceLinkCheck.exe) will be in the bin/Debug/net472 folder.

## More information
Read about Source Link and how you can enable it in your own projects at https://aka.ms/sourcelink
