# 6D — Behavior Settings

Buka tray → Settings → Behavior. Semua kontrol langsung berlaku tanpa restart. Pilihan tetap berlaku ketika Settings ditutup/dibuka lagi atau karakter disembunyikan di tray. Sejak fase 6F, pilihan juga tersimpan setelah keluar dari aplikasi melalui `settings.json`; lihat [PHASE6_RELEASE.md](PHASE6_RELEASE.md).

## Kontrol

| Kontrol | Pilihan / efek |
| --- | --- |
| Enable autonomous behavior | Default ON. OFF menghentikan jalan dan keputusan otonom serta membangunkan karakter; klik, drag, lempar, chat, kedipan, dan pilihan cursor tetap tersedia. |
| Activity level | Calm lebih lama idle, lebih jarang berjalan, dan lebih pendek berjalan; Balanced memakai pacing bawaan; Active lebih sering berjalan, bereaksi pada cursor, dan mencoba berpindah window. |
| Movement speed | Slow / Normal / Fast mengubah kecepatan berjalan menjadi 0,65× / 1× / 1,45× dari 70 piksel/detik. Gravity, lemparan, dan lintasan lompatan tidak berubah. |
| Allow Lu-Knight to sleep | OFF membangunkan karakter yang sedang tidur. |
| Sleep after | 1 min / 2 min / 5 min / Never sejak interaksi terakhir. Default 2 min. Never menonaktifkan tidur otomatis. |
| Nap duration | Short sekitar 3,5–7,5 detik, Normal 7–15 detik, Long 14–30 detik. Perubahan saat tidur memperbarui waktu bangun dari saat pilihan diganti. |
| Explore application windows | Mengizinkan petualangan di tepi window. OFF tetap mengizinkan berjalan di desktop dan mempertahankan collision/pijakan fisik, termasuk window tempat karakter dijatuhkan pengguna. |
| Jump between windows | Mengizinkan lompatan otonom antarpijakan; bergantung pada Explore dan autonomy. |
| Hanging / climbing | Mengizinkan bergantung dan memanjat; terpisah dari izin melompat. |
| Look at cursor | Mengatur arah pandangan. Jika reaksi cursor OFF, mengikuti cursor tidak menghentikan jalan. |
| React when cursor approaches | Mengatur berhenti untuk memperhatikan, mood penasaran/senang, dan gerak telinga ketika cursor dekat. |
| Wake when cursor approaches | Terpisah dari gaze/reaction; jika OFF, klik/drag tetap dapat membangunkan. |
| Reset Behavior to Default | Mengembalikan seluruh pilihan Behavior tanpa mengubah General, startup, atau chat. |

Kontrol yang bergantung pada autonomy/sleep/exploration dinonaktifkan di UI tanpa menghapus pilihan sebelumnya. Pengaturan dapat diubah saat karakter belum pernah ditampilkan dalam sesi startup tersembunyi; pilihan diterapkan saat controller dibuat.

## Pergantian state

Saat autonomy dimatikan, jalan berhenti langsung. Petualangan tepi yang dibatalkan kembali idle; attachment samping atau persiapan lompatan yang dibatalkan dilepas ke physics agar karakter turun secara alami. Lompatan/lemparan yang sudah mengudara menyelesaikan lintasannya. Mengubah preferensi tidak menghapus pause milik chat, drag, physics, atau tray. Mengaktifkan kembali autonomy/sleep memulai waktu tunggu tidur baru.

## Struktur

- `Behaviors/BehaviorSettings.cs`: pilihan produk dan pemilik preferensi selama sesi, terpisah dari state sementara.
- `MainWindow.xaml.cs`: menerapkan perubahan ke controller serta menyimpan pilihan sebelum controller diinisialisasi.
- `Behaviors/BehaviorController.cs`: pacing, kecepatan berjalan, tidur, cursor, dan keputusan otonom.
- `Behaviors/SurfaceBehaviorController.cs`: izin petualangan dan pembatalan attachment/persiapan lompatan.
- `ViewModels/SettingsViewModel.cs` dan `Views/SettingsWindow.xaml`: binding, dropdown, dependensi kontrol, dan reset.

## Verifikasi

```powershell
dotnet build LuKnight.csproj -c Release --no-restore
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --behavior-settings
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --settings
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
```

Tes khusus mencakup autonomy OFF, interaksi manual, cursor independen, tidur/wake/Never, kecepatan, pembatalan petualangan, lintasan physics yang tetap utuh, binding UI, reset, dan preferensi saat Settings dibuka ulang. Render WPF diperiksa pada ukuran normal dan minimum; tombol reset tetap dapat dijangkau dengan scroll. Tes memakai render offscreen dan event sintetis; interaksi desktop native belum diverifikasi langsung.
