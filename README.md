## Motivation

As of [Microsoft.Extensions.DependencyModel 6.0.0][1], getting the DependencyContext of an application deployed as a [single-file][2] is not supported. The code is [properly annotated][3] with the `RequiresAssemblyFiles` attribute so you'll be warned.

```csharp
[RequiresAssemblyFiles("DependencyContext for an assembly from a application published as single-file is not supported. The method will return null. Make sure the calling code can handle this case.")]
public static DependencyContext Default => _defaultContext.Value;
```

This project explores different methods to get a `DependencyContext` of an application published as a single file executable on Linux, macOS and Windows. The goal of this project is to eventually get the best method merged into the official .NET Runtime repository.

## Single-file application overview

The current implementation of `DependencyContext` requires a physical `.deps.json` file on disk, which doesn't exist when deployed as single-file. But the `.deps.json` does exist, it's instead *bundled* (embedded) into the single-file app.

The bundle format is documented in [The Single-file Bundler][4] design document. The code that actually produces the bundle is available in the [Microsoft.NET.HostModel][5] component. Here's the layout of the bundle (copied from the [Manifest][6] class documentation).

```
________________________________________________
AppHost
 
------------Embedded Files ---------------------
The embedded files including the app, its
configuration files, dependencies, and
possibly the runtime. 
 
------------ Bundle Header -------------
    MajorVersion
    MinorVersion
    NumEmbeddedFiles
    ExtractionID
    DepsJson Location [Version 2+]
       Offset
       Size
    RuntimeConfigJson Location [Version 2+]
       Offset
       Size
    Flags [Version 2+]
- - - - - - Manifest Entries - - - - - - - - - - -
    Series of FileEntries (for each embedded file)
    [File Type, Name, Offset, Size information]
 
_________________________________________________
```

We see that the bundle header includes the `.deps.json` offset and size (for version 2+, which means [when targeting .NET 5+][7]). Once the `.deps.json` offset and size are found a `DependencyContext` can be easily constructed with a `DependencyContextJsonReader`.

The *hardest* part of the problem is to figure out the offset of the bundle header within the single-file application. Once the bundle header offset is known, reading the header values is trivial with a `BinaryReader`.

## Implementations

Here are different implementations for finding the  `.deps.json` offset and size. They all come with their downsides and none is perfect. Another implemention that actively cooperates with the CoreCLR to communicate the required values should be attempted.

|          | 👍 Pros                                                | 👎 Cons                                                       |
| -------- | ----------------------------------------------------- | ------------------------------------------------------------ |
| Method 1 | Simple to implement                                   | Slow<br />May be wrong if several occurrences of the bundle marker exist in the single-file app host |
| Method 2 | Safe as the information comes from the CoreCLR itself | Relies on parsing debug logs to retrieve critical information |
| Method 3 | Fast                                                  | Hard to implement, have to rely on 3rd party libraries to parse ELF, Mach-O and PE file formats ([ELFSharp](https://www.nuget.org/packages/ELFSharp/) and [PeNet](https://www.nuget.org/packages/PeNet/))<br />Will probably break in the future as the apphost binaries evolve |
| Method 4 | Safe and fast                                         | None, that would be the perfect solution when implemented    |

### 1. Find the bundle signature

Implemented in the `FindBundleSignature` class.

This is the most straightforward approach. Single-file applications all include a [*bundle marker* of 64 bytes][8] which is the SHA-256 for `.net core bundle`. The bundle header offset is [located 8 bytes before this marker][9].

Note: this is how `Microsoft.NET.HostModel` [is implemented][10] but this package [is not intended as a public API][11], it's only intended to use from the SDK.

### 2. Get the values from the apphost logs

Implemented in the `CaptureAppHostLogs` class.

This is a very convoluted way to get the `.deps.json` offset and size that I would never use in production but is an interesting experiment. The apphost is run with the `COREHOST_TRACE` environment variable set to `1`, producing logs on stderr. For example:

```
[…]
Bundle Header Offset: [42b4ada]
--- Invoked hostfxr_main_bundle_startupinfo [commit hash: static]
Mapped application bundle
Unmapped application bundle
Single-File bundle details:
DepsJson Offset:[42a4888] Size[10252]
RuntimeConfigJson Offset:[a6ac50] Size[10f]
.net core 3 compatibility mode: [No]
[…]
```

Redirecting stderr and reading the logs makes it possible to catch the value we are interested in:

> DepsJson Offset:[**42a4888**] Size[**10252**]

### 3. Get the bundle header offset by parsing the apphost file format

Implemented in the `ParseExecutableFileFormat` class.

The idea is to find the `bundle_marker_t::header_offset()::placeholder` symbol in the executable symbol table. This directly points to the bundle header offset.

Like the second option, this should not be used in production since the symbol could not exist in the binary. Also, reading the symbol table is a non trivial task and requires specific code for reading ELF (Linux), Mach-O (macOS) and PE (Windows) files.

For example, on macOS, with the app published with `dotnet publish -c Release -f net6.0 -r osx-x64 --self-contained`

Find the symbol address (`000000010086eea0`)
```
nm SingleFileAppDependencyContext | grep placeholder
000000010086eea0 d __ZZN15bundle_marker_t13header_offsetEvE11placeholder
```

Get information about the `__DATA,__data` section where the symbol is located.
```
otool -lv SingleFileAppDependencyContext | grep __data -A 4
  sectname __data
   segname __DATA
      addr 0x000000010086ee40
      size 0x0000000000004c8c
    offset 8842816
```

Compute the file offset.
```
symbolAddress = 0x000000010086eea0
dataAddress = 0x000000010086ee40
dataOffset = 0x86ee40 (8842816)
fileOffset = dataOffset + symbolAddress - dataAddress = 0x86eea0 = 8842912
```

Finally read the bundle header offset:
```
xxd -s 8842912 -l 8 -p SingleFileAppDependencyContext
da4a2b0400000000
```

Convert this 64-bits little-endian value:
```
python3 -c "from struct import unpack; print(unpack('Q', bytes.fromhex('da4a2b0400000000'))[0])"
69946074
```

`69946074` is `0x42b4ada` in hexadecimal which is the bundle header offset. This can be verified by running `COREHOST_TRACE=1 ./SingleFileAppDependencyContext`

> The managed DLL bound to this executable is: 'SingleFileAppDependencyContext.dll'
> Detected Single-File app bundle
> Using internal fxr
> […]
> Bundle Header Offset: [**42b4ada**]
> […]
> DepsJson Offset:[42a4888] Size[10252]
> RuntimeConfigJson Offset:[a6ac50] Size[10f]

For other formats (ELF and PE) the apphost  symbols are stripped so a hardcoded offset from the beginning of the data section is used instead. For Mach-O, the hardcoded offset is used as a fallback if the `bundle_marker_t::header_offset()::placeholder` symbol is not found.

### 4. Active cooperation with the CoreCLR

Not yet implemented.

This is probably the best solution that should be attempted. The CoreCLR should provide the `.deps.json` offset and size (or even maybe more, such as the whole [bundle::info][13]) and managed code should access it, maybe through [FCall or QCall][12]. I'm not familiar with the subject so I'll have to investigate.

[1]: https://www.nuget.org/packages/Microsoft.Extensions.DependencyModel/6.0.0
[2]: https://docs.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
[3]: https://github.com/dotnet/runtime/blob/v6.0.3/src/libraries/Microsoft.Extensions.DependencyModel/src/DependencyContext.cs#L53-L54
[4]: https://github.com/dotnet/designs/blob/main/accepted/2020/single-file/bundler.md
[5]: https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/Bundle/
[6]: https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs#L17-L50
[7]: https://github.com/dotnet/runtime/blob/v6.0.3/src/native/corehost/bundle/header.h#L56
[8]: https://github.com/dotnet/runtime/blob/v6.0.3/src/native/corehost/apphost/bundle_marker.cpp#L19-L23
[9]: https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs#L182-L191
[10]: https://github.com/dotnet/runtime/blob/v6.0.3/src/installer/managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs#L214-L246
[11]: https://github.com/dotnet/runtime/pull/67386#issuecomment-1087407963
[12]: https://github.com/dotnet/runtime/blob/v6.0.3/docs/design/coreclr/botr/corelib.md#calling-from-managed-to-native-code
[13]: https://github.com/dotnet/runtime/blob/v6.0.3/src/native/corehost/bundle/info.h#L10-L13
