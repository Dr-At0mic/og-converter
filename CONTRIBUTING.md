# Contributing Guidelines

Thanks for improving this project! 

1. Fork the repo and make your updates.
2. Submit a Pull Request.
3. By contributing, you agree that your code will be licensed under the GNU GPLv3.

## Building and testing

The repository holds the shared engine (`src/OgConverter.Core`), the Windows app (`src/OgConverter.Windows`, WPF) and the
Linux app (`src/OgConverter.Linux`, Avalonia, **beta**). You need the [.NET 10 SDK](https://dotnet.microsoft.com/).

- **Windows:** `dotnet build og-converter.sln -c Release` builds all three. Then run the self-test directly (the app is a WinExe, so
  `dotnet run` hides its output): `src\OgConverter.Windows\bin\Release\net10.0-windows10.0.19041.0\og-converter.exe --test`.
- **Linux:** the Windows project doesn't build there, so build the Linux app on its own:
  `dotnet build src/OgConverter.Linux/OgConverter.Linux.csproj -c Release`, then run its `og-converter --test`.

Please keep `OgConverter.Core` free of Windows APIs (WPF, WinRT, `Microsoft.Win32`, P/Invoke, COM): platform code goes in the app
projects behind the interfaces in `Core/Platform`. Changes made for Linux must not change how the Windows app behaves.

## Reporting bugs

Open an issue with what you did, what you expected and what happened. For the Linux beta, include your distribution, desktop
(GNOME, KDE…) and whether it's Wayland or X11.
