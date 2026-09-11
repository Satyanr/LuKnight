# 6A — System Tray

Jalankan `bin/Release/net10.0-windows/LuKnight.exe` setelah build. Aplikasi memakai ikon kelinci dari sprite yang sama dengan karakter. Ikon dapat berada di area ikon tersembunyi (`^`) di samping jam Windows.

Klik kanan ikon untuk membuka menu:

- **Hide / Show Lu-Knight**: sembunyikan atau tampilkan karakter. Label mengikuti visibility window.
- **Open Chat**: tampilkan karakter dan buka chat tanpa menutup chat yang sudah terbuka.
- **Settings**: buka control center General, Behavior, AI & Chat, dan About. Instance yang sudah terbuka dipulihkan dan difokuskan, termasuk ketika karakter tersembunyi.
- **Restart Lu-Knight**: jalankan ulang executable dengan argumen yang sama, lalu tutup proses lama. Launch melalui `dotnet LuKnight.dll` juga didukung.
- **Exit**: tutup aplikasi, batalkan permintaan chat aktif, dan bersihkan ikon/menu tray.

Double-click kiri menampilkan karakter. Alt+F4 menyembunyikan karakter ke tray; Exit benar-benar menutup aplikasi. Windows logout/shutdown tetap diizinkan.

Saat disembunyikan, chat ditutup, mouse capture dilepas, dan behavior serta physics ditangguhkan. Show tidak memindahkan karakter ke dasar desktop atau mengganti pose fisiknya. Pemanggilan Loaded berulang tidak membuat controller baru. Jika pembuatan tray melempar error, karakter tetap tersedia di taskbar dan close menutup aplikasi.

Implementasi: `Services/TrayIconService.cs` mengelola ikon/menu, `App.xaml.cs` menghubungkan perintah serta lifecycle aplikasi, dan `MainWindow.xaml.cs` mengelola visibility/chat.

```powershell
dotnet build LuKnight.csproj -c Release
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --tray
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
```

Tes tray memeriksa dispatch menu, double-click, label visibility, disposal, restart arguments, kepemilikan pause, penghentian physics, dan lifecycle controller menggunakan event sintetis. Tampilan ikon di Explorer dan klik mouse langsung belum diverifikasi otomatis. Pemeriksaan desktop: Show/Hide berulang, Open Chat saat tersembunyi, Alt+F4, Restart, lalu Exit; pastikan hanya satu karakter tersisa setelah restart dan tidak ada proses aktif setelah Exit.
