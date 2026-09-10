# LuKnight: sprite hidup sesuai Reference

Renderer utama tetap `CharacterRenderMode.Sprite`; model 3D tidak digunakan. Referensi identitas adalah `Assets/Characters/LuKnight/Reference/LuKnightReference.png`.

## Jalan dan memanjat

`Visuals/SpritePuppet.cs` merakit lima bagian PNG: kepala/tubuh, lengan dekat/jauh, dan kaki dekat/jauh. Semua adalah gambar 2D. Wajah dan badan memakai tampak samping kanan; transform mirror memberikan arah kiri.

Kedua kaki bergerak berlawanan fase, dengan kaki ayun terangkat bergantian. Kedua lengan berayun berlawanan dengan kaki. Siklus jalan 0.8 detik; memanjat 1.1 detik dengan pergantian tangan dan kaki. Bagian jauh sedikit lebih gelap sehingga kedua kaki dapat dibedakan. Pangkal kaki berada di belakang badan, dan bagian bulat sambungan dipangkas saat import supaya tidak terlihat seperti sendi terpisah.

Rig memakai kanvas transparan tetap 510 x 660. Pergantian sudut sendi tidak mengubah ukuran ImageSource maupun membuat WPF menyesuaikan ulang skala. Motion berjalan pada render clock yang sama dengan sprite player; tidak ditumpuk dengan storyboard bob/squash jalan lama. Tidak ada decode gambar atau blending piksel dalam update rig.

## Pose dan ekspresi lainnya

Paket runtime berisi 68 PNG: 56 frame state (8 untuk setiap state), 7 ekspresi, dan 5 bagian rig. Rangkaian Walk/Climbing disimpan sebagai sumber pose; runtime memakai rig agar kaki/tangan bergerak secara independen dan kontinu.

- Idle: napas, perubahan posisi kepala yang kecil, dan telinga bergerak.
- Blink: kedua mata menutup lalu membuka, terpisah dari ekspresi wink. Mengikuti jadwal behavior yang sudah ada.
- Twitch: rangkaian gerak telinga, kembali ke idle setelah selesai.
- Sleep: napas pelan, mata tertutup, telinga turun.
- Grabbed: kaki bergerak bergantian saat tubuh tergantung.
- Falling: reaksi wajah, kaki di udara dan telinga bergerak.
- Hanging: kedua tangan di atas kepala, badan dan kaki bereaksi.

Full-body PNG memakai kanvas 510 x 660 dan landmark kepala/titik pijak yang dikalibrasi per atlas. Tampak samping mempunyai ukuran proyeksi dan pusatnya sendiri. Pose tidak diperbesar berdasarkan batas siluet per frame. Ekspresi tubuh hanya dipakai saat Idle agar tidak mengganti pose fisik yang sedang aktif.

`SpriteAnimationPlayer` memuat cache sebelum playback, memakai `CompositionTarget.Rendering`, mengabaikan callback duplikat, dan membatasi delta sesudah stall ke 50 ms. Rangkaian bitmap memakai interpolasi premultiplied RGBA; pergantian clip memakai blend 100 ms. State yang sama tidak me-reset phase. Unload menghentikan playback dan melepas cache/subscription. Vector tetap menjadi fallback apabila aset gagal dibaca.

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

## Verifikasi

284 pemeriksaan renderer lolos: state/mood, arah, lifecycle, cadence 30/60/120/144 Hz, callback duplikat, stall, blend, phase boundary, alpha bounds, kedipan dua mata, runtime memakai rig, dan kedua kaki bergerak berlawanan pada fase yang berbeda. Kanvas rig diuji sepanjang 24 sampel siklus untuk kedua arah, tanpa clipping. 63 full-body PNG lolos validasi kanvas/transparansi. Kepala Idle/Expressions terukur 241-249 px, pusat X 249.5-253.5 px.

Update sendi rig terukur sekitar 0.02 ms untuk Walk dan 0.008 ms untuk Climb dalam benchmark offscreen pada sesi pengembangan. Angka ini bukan pengukuran FPS desktop.

Tes cursor/drag dan native window memerlukan Windows interaktif dan belum dijalankan di sesi tool ini:

```powershell
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --desktop
```
