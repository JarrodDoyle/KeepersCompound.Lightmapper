# KeepersCompound: Lightmapper

## Description

[TTLG Release Thread](https://github.com/JarrodDoyle/KeepersCompound.Lightmapper)

KCLight is an external lightmapping tool for [NewDark 1.27](https://www.ttlg.com/forums/showthread.php?t=149856) fan mission. DromEd's built-in lighting is single-threaded, has a number of bugs, and scales extremely poorly with lightmap scale and large open spaces. KCLight uses a multi-threaded approach with a modern raytracing library to improve lighting times, reduce lighting artifacts, and often improves in-game performance in complex scenes.

## Features
- All DromEd lighting functionality
- Massively improved lighting times
- Better light culling, improving performance in large-scale city maps and eliminating object lighting errors
- Additional warnings to mappers for mis-configured lights
- Produces generally more accurate shadows

## Usage
Download the [latest release](https://github.com/JarrodDoyle/KeepersCompound.Lightmapper/releases/latest) and unzip it somewhere. Open a console in the unzipped folder and run `KeepersCompound.Lightmapper --help` to see the help screen:

```
Compute lightmaps for a NewDark .MIS/.COW

Usage:
  KeepersCompound.Lightmapper <install-path> <campaign-name> <mission-name> 
  [options]

Arguments:
  <install-path>   The path to the root Thief installation. [required]
  <campaign-name>  The folder name of the fan mission. For OMs this is blank. 
                   [required]
  <mission-name>   The name of the mission file including extension. [required]

Options:
  -f, --fast-pvs     Use a fast PVS calculation with looser cell light indices. 
                     [default: False]
  -o, --output-name  Name of output file excluding extension. [default: kc_lit]
  -?, -h, --help     Show help and usage information
  -v, --version      Show version information
```

## Building

This project requires the [.NET 9.0 runtime and SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0). Raytracing uses a forked version of [TinyEmbree](https://github.com/pgrit/TinyEmbree) with backface culling enabled, a pre-built package can be found in `LocalPackages`.

## License

Distributed under the MIT License. See `LICENSE` for more information.