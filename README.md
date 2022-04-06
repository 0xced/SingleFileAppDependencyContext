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

Here are different implementations for finding the  `.deps.json` offset and size.

The first implementation is a serious, production ready implementation. The second implementation is not really serious and the third implementation is just an excuse to play around with [ELFSharp](https://www.nuget.org/packages/ELFSharp/).

### 1. Find the bundle signature

Implemented in the `FindBundleSignature` class.

This is the most straightforward approach. Single-file applications all include a [*bundle marker* of 64 bytes][8] which is the SHA-256 for `.net core bundle`. The bundle header offset is [located 8 bytes before this marker][9].

Note: this is how `Microsoft.NET.HostModel` [is implemented][10] but this package [is not intended as a public API][11], it's only intended to use from the SDK.

### 2. Get the values from the apphost logs

Implemented in the `CaptureAppHostLogs` class.

This is a very convoluted way to get the `.deps.json` offset and size that I would never use in production but is an interesting experiment. The apphost us run with the `COREHOST_TRACE` environment variable set to `1`, producing logs on stderr. For example:

```
[…]
Bundle Header Offset: [99320]
Tracing enabled @ Tue Apr  5 20:37:03 2022 UTC
--- Invoked hostfxr_main_bundle_startupinfo [commit hash: c24d9a9c91c5d04b7b4de71f1a9f33ac35e09663]
Mapped application bundle
Unmapped application bundle
Single-File bundle details:
DepsJson Offset:[1f4c0] Size[25da]
RuntimeConfigJson Offset:[21a9a] Size[8b]
.net core 3 compatibility mode: [No]
[…]
```

Redirecting stderr and reading the logs makes it possible to catch the value we are interested in:

> DepsJson Offset:[**1f4c0**] Size[**25da**]

### 3. Get the bundle header offset by reading the apphost symbol

Implemented in the `FindBundleHeaderOffsetSymbol` class.

The idea is to find the `bundle_marker_t::header_offset()::placeholder` symbol in the executable symbol table. This directly points to the bundle header offset.

Like the second option, this should not be used in production since the symbol could be stripped from the binary. Also, reading the symbol table is a non trivial task and requires specific code for reading ELF (Linux), Mach-O (macOS) and PE (Windows) files.

For example, on macOS:

Find the symbol address (`00000001000106e0`)
```
nm -m SingleFileApp | grep placeholder          
00000001000106e0 (__DATA,__data) non-external __ZZN15bundle_marker_t13header_offsetEvE11placeholder
```

Get information about the `__DATA,__data` section where the symbol is located.
```
otool -lv SingleFileApp | grep __data -A 4 
  sectname __data
   segname __DATA
      addr 0x00000001000106e0
      size 0x00000000000004a1
    offset 67296
```

Compute the file offset. Note that it's not guaranteed that the symbol is the first one in the data section. The symbol address matching the data section is a coincidence and could change in the future.
```
symbolAddress = 0x00000001000106e0;
dataAddress = 0x00000001000106e0 - 67296 = 0x0000000100000000
fileOffset = symbolAddress - dataAddress = 0x106E0 = 67296
```

Finally read the bundle header offset:
```
xxd -s 67296 -l 8 -p SingleFileApp
2093090000000000
```

Convert this 64-bits little-endian value:
```
python3 -c "from struct import unpack; print(unpack('Q', bytes.fromhex('2093090000000000'))[0])"
627488
```

`627488` is `99320` in hexadecimal which is the bundle header offset.

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
