# libDemech2Sharp

A C# port of [demech2](https://github.com/anpage/demech2), the decompilation of MechWarrior 2, packaged as a
Unity package (`com.moonbunny.libdemech2sharp`) that builds `Demech2.dll`.

The library talks to its host game only through the interfaces in `Runtime/Host/`, so it can be rebuilt and
replaced independently of the game that uses it.

## Use in Unity

Add it as a git dependency in `Packages/manifest.json`:

    "com.moonbunny.libdemech2sharp": "https://github.com/JennaScvl/libDemech2Sharp.git"

or clone it (e.g. as a git submodule) into your project's `Packages/` folder as an embedded package.

## License

GNU Lesser General Public License v3.0 — see [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md).
