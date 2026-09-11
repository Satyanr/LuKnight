# LuKnight: sprite hidup sesuai Reference

Renderer utama tetap `CharacterRenderMode.Sprite`; model 3D tidak digunakan. Referensi identitas adalah `Assets/Characters/LuKnight/Reference/LuKnightReference.png`.

## Jalan dan memanjat

`Visuals/SpritePuppet.cs` merakit lima bagian PNG: kepala/tubuh, lengan dekat/jauh, dan kaki dekat/jauh. Semua adalah gambar 2D. Wajah dan badan memakai tampak samping kanan; transform mirror memberikan arah kiri.

Kedua kaki bergerak berlawanan fase, dengan kaki ayun terangkat bergantian. Kedua lengan berayun berlawanan dengan kaki. Siklus jalan 0.8 detik; memanjat 1.1 detik dengan pergantian tangan dan kaki. Bagian jauh sedikit lebih gelap sehingga kedua kaki dapat dibedakan. Pangkal kaki berada di belakang badan, dan bagian bulat sambungan dipangkas saat import supaya tidak terlihat seperti sendi terpisah.

Rig memakai kanvas transparan tetap 510 x 660. Pergantian sudut sendi tidak mengubah ukuran ImageSource maupun membuat WPF menyesuaikan ulang skala. Motion berjalan pada render clock yang sama dengan sprite player; tidak ditumpuk dengan storyboard bob/squash jalan lama. Tidak ada decode gambar dalam update rig. Gerak wajah memperbarui bitmap kepala pada clock yang sama, tanpa me-reset fase kaki.

## Pose dan ekspresi lainnya

Paket runtime berisi 68 PNG: 56 frame state (8 untuk setiap state), 7 ekspresi, dan 5 bagian rig. Rangkaian Walk/Climbing disimpan sebagai sumber pose; runtime memakai rig agar kaki/tangan bergerak secara independen dan kontinu.

- Idle: satu pose tubuh tetap dengan napas halus dari transform WPF. Mata dan telinga bergerak lokal melalui `SpriteFace`, sehingga tidak ada pergantian seluruh tubuh yang menimbulkan ghosting.
- Blink: kelopak menutup lalu membuka, mengikuti jadwal behavior. Berlaku pada wajah Idle dan rig Walk/Climbing tanpa mengganti clip. Pose fisik lainnya tetap memakai ekspresi bawaan rangkaiannya.
- Twitch: deformasi kecil di area telinga; dapat berjalan bersamaan dengan kedipan, arah pandangan, dan langkah.
- Sleep: napas pelan, mata tertutup, telinga turun.
- Grabbed: kaki bergerak bergantian saat tubuh tergantung.
- Falling: reaksi wajah, kaki di udara dan telinga bergerak.
- Hanging: kedua tangan di atas kepala, badan dan kaki bereaksi.

Full-body PNG memakai kanvas 510 x 660 dan landmark kepala/titik pijak yang dikalibrasi per atlas. Tampak samping mempunyai ukuran proyeksi dan pusatnya sendiri. Pose tidak diperbesar berdasarkan batas siluet per frame. Saat Idle, hanya bagian interior wajah dari PNG ekspresi yang digunakan; torso dan kaki tidak berganti gambar. Kelopak dan arah pandangan menjaga alpha siluet kepala agar tidak menarik background transparan ke dalam wajah.

`SpriteAnimationPlayer` memuat cache sebelum playback, memakai `CompositionTarget.Rendering`, mengabaikan callback duplikat, dan membatasi delta sesudah stall ke 50 ms. Rangkaian bitmap memakai interpolasi premultiplied RGBA; pergantian clip memakai blend 100 ms. State yang sama tidak me-reset phase. Unload menghentikan playback dan melepas cache/subscription. Vector tetap menjadi fallback apabila aset gagal dibaca.

## Cursor, klik, dan behavior

`BehaviorController.ObservePointer` menerima event mouse WPF langsung, selain pembacaan cursor global. Koordinat dikonversi langsung ke `CharacterView` agar offset window dan DPI tetap tepat. Saat cursor mendekat, karakter berhenti berjalan, menatap cursor, dan bereaksi melalui mood serta telinga. Hover menjaga state Grabbed/Falling/Hanging/Climbing. Pandangan pada rig yang dimirror tetap mengikuti arah layar.

LookSide, LookDown, dan EdgePeek menggerakkan mata, lalu kembali ke netral. Pause chat menghentikan gerak otonom tetapi perhatian, kedipan, dan durasi reaksi tetap berjalan. Sprite menerima hit testing; event mouse mencapai handler klik/drag. Jika capture gagal atau terlepas, pemilik pause UserDrag dilepas agar behavior tidak tertinggal dalam keadaan pause. Pelepasan ketika sedang drag menyerahkan kontrol kembali ke physics.

## Import dan preview

Sumber ImageGen terbaru dan prompt: `output/imagegen/alive-sprites/`. Importer menggunakan `tools/ReferenceSpriteImport.cs` untuk alpha/chroma key, membuang speck terpisah, lalu menempatkan aset pada kanvas tetap. Output disiapkan di staging sebelum mengganti paket runtime.

```powershell
powershell -NoProfile -File tools/Import-SpriteSheets.ps1
powershell -NoProfile -File tools/Import-SpriteSheets.ps1 -RigOnly
powershell -NoProfile -File tools/Build-SpritePreview.ps1
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
dotnet build LuKnight.csproj
```

Preview runtime:

- `output/sprites/walk-directions.gif`: animasi jalan ke kiri dan kanan.
- `output/sprites/climb-directions.gif`: animasi memanjat dua arah.
- `output/sprites/directional-motion.png`: sampel empat fase kedua arah.
- `output/sprites/preview.png`: semua state dirender lewat WPF.
- `output/sprites/sprite_pack_preview.png`: seluruh rangkaian PNG sumber.
- `output/sprites/attention-check.png`: pandangan idle, kedipan, reaksi wajah pada rig berjalan, dan seluruh mood.

## Verifikasi

329 pemeriksaan lolos pada build Release, termasuk 45 pemeriksaan khusus idle/perhatian/input. Hasil visual akhir ditinjau pada contact sheet runtime.

Suite mencakup state/mood, arah, lifecycle, cadence 30/60/120/144 Hz, callback duplikat, stall, blend, phase boundary, alpha bounds, runtime rig, dan kedua kaki yang bergerak berlawanan. Kanvas rig diuji sepanjang 24 sampel siklus untuk kedua arah, tanpa clipping. Pemeriksaan perhatian mencakup alpha kepala pada sembilan arah pandangan saat berkedip, kestabilan tubuh idle, hover, glance, input routing, pelepasan capture, serta reaksi saat chat pause. Pemeriksaan WPF ini memakai render offscreen dan event sintetis.

Benchmark sendi rig yang dicetak suite tidak mencakup animasi wajah dan bukan pengukuran FPS desktop.

Tes cursor/drag dan native window memerlukan Windows interaktif dan belum dijalankan di sesi tool ini:

```powershell
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --desktop
```
