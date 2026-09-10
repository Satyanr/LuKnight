# Sprite dan struktur kode Lu-Knight

## Aset runtime

Semua path relatif terhadap folder executable. PNG disalin oleh `LuKnight.csproj` sebagai Content dengan `PreserveNewest`.

| Folder/state | Frame saat ini | FPS | Loop |
| --- | ---: | ---: | --- |
| Idle | 4 | 5 | Ya |
| Walk | 4 | 7 | Ya |
| Sleep | 4 | 3 | Ya |
| Grabbed | 4 | 6 | Ya |
| Falling | 4 | 7 | Ya |
| Hanging | 4 | 5 | Ya |
| Climbing | 4 | 7 | Ya |

Gunakan nama berurutan dengan padding, misalnya `walk_000.png`, `walk_001.png`. Factory membaca PNG langsung di folder state dan mengurutkan nama secara ordinal. Jangan simpan preview atau pose lain di folder state.

`Expressions` berisi gambar seluruh tubuh, bukan overlay wajah. Ekspresi hanya menggantikan Idle; state fisik selalu memakai frame aksinya sendiri.

| PNG | Pemicu |
| --- | --- |
| happy | Mood Happy |
| surprised | Mood Surprised |
| dizzy | Mood Dizzy |
| sad | Mood Confused atau Sad |
| determined | Mood Thinking atau Determined |
| angry | Mood Angry |
| wink | Blink saat Idle dengan mood Neutral/Happy, atau mood Wink |

`Sad`, `Angry`, `Determined`, dan `Wink` tersedia melalui `CharacterView.SetMood(...)`. Perilaku otomatis tidak dibuat marah tanpa pemicu baru. Blink menampilkan wink selama 180 ms, kemudian memulihkan clip Idle/mood. State berubah membatalkan wink. Sleep tidak menerima mood non-neutral.

`Reference/luknight_master.png` dan `Reference/sprite_pack_preview.png` adalah referensi desain, bukan frame animasi runtime.

## Pembagian kode

- `Behaviors/BehaviorController.cs`: keputusan aktivitas, nap, cursor, dan mood; meneruskan event terrain.
- `Behaviors/SurfaceBehaviorController.cs`: support window, edge, hang, climb, dan rencana jump.
- `Physics/CharacterPhysicsController.cs`: grab, throw, gravity, collision, dan landing.
- `Services/SurfaceNavigationService.cs`: memilih target dan menghitung velocity jump.
- `Views/CharacterView.xaml.cs`: state/mood publik, dispatch renderer, dan animasi vector.
- `Views/CharacterView.Sprites.cs`: registrasi state/ekspresi, hybrid renderer, sprite motion, wink, serta cleanup.
- `Visuals/SpriteClipFactory.cs`: mengubah folder menjadi clip.
- `Visuals/SpriteAnimationPlayer.cs`: playback, cache bitmap, serta notifikasi frame gagal.
- `MainWindow.xaml.cs`: menghubungkan behavior, physics, chat, dan lifecycle aplikasi.

Preferensi renderer dipisahkan dari renderer aktif. State tanpa clip atau frame gagal kembali ke vector; state berikutnya mencoba sprite lagi jika preferensinya Sprite. `SetRenderMode(Vector)` tetap memaksa vector.

Cache bitmap memakai path, ukuran file, dan waktu modifikasi. Frame yang tidak berubah dipakai ulang; file hilang atau berubah tetap diperiksa. Player membedakan objek clip, sehingga clip baru dengan nama sama dapat menggantikan clip lama. Unload melepas player, timer wink, dan motion; load memulihkan state saat ini.

## Verifikasi perubahan

Harness WPF menguji 28 frame state dan tujuh file ekspresi: decoding, urutan, loop/cache, prioritas state fisik, wink dan pemulihannya, facing, fallback, mode vector eksplisit, serta unload/load. Pemeriksaan visual langsung terhadap kelancaran animasi desktop belum dilakukan.
