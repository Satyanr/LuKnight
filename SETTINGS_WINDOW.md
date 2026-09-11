# 6B — Settings Window

Buka **Settings** dari menu klik kanan ikon tray. Window Settings berdiri sendiri: tetap dapat dibuka ketika karakter disembunyikan, membuka Settings lagi memfokuskan instance yang sama, dan menutup Settings tidak menutup Lu-Knight. Window yang diminimalkan dipulihkan ketika dibuka dari tray.

## Fitur aktif

- **General**: Always on top, Show Lu-Knight, Reset character position, renderer aktual, dan status aplikasi. Reset membatalkan drag/fall serta target lompatan, melepas pijakan window, lalu menempatkan karakter di tengah pijakan desktop pada monitor karakter. Chat yang sedang berjalan tetap dipertahankan.
- **Behavior**: halaman fondasi untuk kontrol aktivitas, gerakan, tidur, eksplorasi window, dan interaksi cursor.
- **AI & Chat**: status layanan, provider, model aktif, dan status API dari runtime. Key yang tersedia tidak dianggap sebagai koneksi terverifikasi. Respons sukses, permintaan berlangsung, dan kegagalan ditampilkan sesuai status chat; API key tidak ditampilkan atau disimpan di sini.
- **About**: version, build/configuration/architecture, copyright dari metadata assembly, serta tautan repository. Tidak ada pemeriksaan jaringan otomatis saat Settings dibuka.

Always on top dan visibility langsung berlaku serta tetap mengikuti perubahan dari tray. Nilainya hanya berlaku selama sesi aplikasi; persistence belum dibuat. Start with Windows dan Start hidden disiapkan sebagai kontrol nonaktif untuk 6C. Kontrol behavior menunggu 6D, konfigurasi AI menunggu 6E, penyimpanan menunggu 6F, dan Check for Updates tetap nonaktif sampai 6H.

## Struktur

- `ViewModels/SettingsViewModel.cs`: snapshot runtime, binding, navigasi, perintah, dan metadata About.
- `Views/SettingsWindow.xaml`: empat halaman, sidebar dengan indikator pilihan, gaya kontrol, serta scroll untuk ukuran window kecil.
- `Views/SettingsWindow.xaml.cs`: koneksi ke mascot, refresh status setiap 500 ms selama terlihat, dan cleanup saat ditutup.
- `App.xaml.cs`: satu instance Settings, restore/focus, serta lifecycle aplikasi.

Pemanggilan `_tray.Show()` dipertahankan setelah tray dibuat agar menu Settings benar-benar dapat diakses.

## Verifikasi

```powershell
dotnet build LuKnight.csproj -c Release
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --settings
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
```

439 pemeriksaan lolos, termasuk 30 pemeriksaan Settings: binding dua arah, update runtime tanpa rekursi, navigasi, API status, kontrol yang belum aktif, scroll ukuran minimum, cleanup timer, singleton/reopen, independensi dari mascot, dan reset physics. Empat halaman ditinjau melalui render WPF pada `output/sprites/settings-0.png` sampai `settings-3.png`.

Tes memakai render offscreen dan event sintetis. Fokus window, klik tray langsung, serta perpindahan karakter pada desktop/multi-monitor belum diverifikasi secara interaktif.
