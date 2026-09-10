# Rendering Lu-Knight: model 3D dan sprite cadangan

Renderer default sekarang `CharacterRenderMode.Sprite`. Mode 3D tetap tersedia melalui `SetRenderMode(CharacterRenderMode.Model3D)`. Pada mode 3D, Lu-Knight memakai mesh 3D WPF dengan kamera orthographic, material, pencahayaan, dan sendi untuk kepala, telinga, lengan, serta kaki. Ini model prosedural dalam kode, bukan file Blender/GLB atau sprite yang diberi efek perspektif. Tidak ada package tambahan.

## Ukuran dan animasi

Satu geometri dipakai untuk Idle, Walk, Sleep, Grabbed, Falling, Hanging, dan Climbing. Kamera serta skala tubuh tetap; perubahan pose berasal dari rotasi/translasi sendi. Pergantian state dan mood mempertahankan pose saat ini, lalu interpolasi berjalan berdasarkan waktu. Mood mengubah wajah, bukan mengganti gambar seluruh tubuh.

Animasi, sampling cursor saat drag, gerak jatuh, jalan, serta climb mengikuti `CompositionTarget.Rendering`. Callback duplikat untuk frame yang sama diabaikan. Delta animasi dibatasi saat render tersendat. Posisi drag mengikuti cursor langsung; goyangan visual dibatasi di sekitar kepala. Smoothing kecepatan lempar berbasis waktu. Jarak pecahan piksel saat berjalan dipertahankan agar kecepatan tidak berubah pada layar 30/60/120/144 Hz.

Keputusan behavior tetap memakai timer 33 ms. Gravity, target jump, dan terrain collision tetap ditangani controller physics/navigation. Manual grab tetap membersihkan target autonomous.

## Struktur kode

- `Visuals/Model3DGeometry.cs`: mesh dan material yang dibuat sekali lalu dibekukan.
- `Visuals/CharacterModel3DPlayer.cs`: rig, ekspresi, blending pose, render clock, serta lifecycle.
- `Views/CharacterView.xaml.cs`: API state/mood dan dispatch ke renderer aktif.
- `Views/CharacterView.Sprites.cs`: pemilihan renderer, registry sprite cadangan, dan cleanup.
- `Behaviors/BehaviorController.cs`: keputusan aktivitas/mood dan penjadwalan gerak saat render.
- `Behaviors/SurfaceBehaviorController.cs`: support, edge, hang, climb, dan persiapan jump.
- `Physics/CharacterPhysicsController.cs`: cursor/grab, lempar, falling, collision, dan landing.
- `MainWindow.xaml.cs`: mouse capture dan penghubung behavior, physics, serta chat.

`SetRenderMode(Vector)` dan `SetRenderMode(Sprite)` masih tersedia. Renderer yang tidak aktif dihentikan; unload melepas subscription render, timer, dan scene. Model tidak membaca PNG selama animasi.

## Aset PNG cadangan

112 frame state, tujuh ekspresi, dan master kini dirender ulang dari model 3D yang sama pada kanvas transparan 510 x 660, dengan kamera tetap. Tidak ada crop atau resize per siluet. Ukuran kepala/tubuh tetap konsisten; tinggi siluet boleh berubah secara alami saat duduk atau mengangkat kaki.

Setiap folder state berisi 16 frame. Mode Sprite memakai satu frame Grabbed agar pose tetap stabil saat drag; gerak visualnya berasal dari transform. FPS Walk 25.6, Climbing sekitar 20.37, dan pose bernapas sekitar 5.60, sesuai periode ekspor. Mode 3D tidak dibatasi oleh FPS sprite tersebut.

Untuk regenerasi dan pemeriksaan:

```powershell
./tools/Export-ModelSprites.ps1
```

Script menjalankan pemeriksaan WPF dan mengekspor model, lalu membuat contact sheet. `Import-SpriteSheets.ps1` serta `output/imagegen/sprite-sources` hanya menyimpan pipeline/sumber AI sebelumnya; importer lama melakukan fit per siluet dan tidak dipakai untuk paket baru.

Preview model: `output/model3d/preview.png`.
Preview semua PNG: `Assets/Characters/LuKnight/Reference/sprite_pack_preview.png`.

## Verifikasi

```powershell
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj
dotnet clean LuKnight.csproj
dotnet build LuKnight.csproj
dotnet run --project LuKnight.csproj --no-build
```

Harness memeriksa semua state/mood, geometri tetap, transparansi tepi, kedua arah hadap, transisi tanpa reset, skala saat grab, reset target autonomous, release ke Falling, cadence 30/60/120/144 Hz, callback duplikat, fractional walking, pause saat drag, dan unload/reload. Uji cursor/native window memerlukan akses desktop Windows. Benchmark transform offscreen bukan pengukuran FPS layar.

API yang digunakan: [WPF 3D overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/3-d-graphics-overview) dan [CompositionTarget.Rendering](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget).

Verifikasi terakhir: 203 pemeriksaan logic/render dan 119 pemeriksaan aset lolos; clean/build berhasil tanpa warning/error. Verifikasi tersebut dilakukan saat default masih Model3D; default kini Sprite sesuai pilihan pengguna. Pemeriksaan visual drag melalui computer-use belum selesai karena koneksi native pipe tidak tersedia; hasil benchmark offscreen tidak menjamin FPS desktop.
