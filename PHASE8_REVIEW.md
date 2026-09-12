# Review Phase 8 — 12 September 2026

Review mencakup alur assistant, context sources, desktop actions, penyajian respons,
persistensi, serta pemeriksaan regresi rendering, physics, settings, startup dan update.

| Bagian | Hasil review dan perbaikan |
| --- | --- |
| 8A Application Awareness | Routing, registry tool, snapshot aplikasi dan pengaturan nonaktif tercakup tes. Informasi tetap sebatas nama proses/kategori aplikasi yang terlihat. |
| 8B File Context | Ukuran diperiksa pada handle yang dibuka; baca dibatasi 24.001 karakter untuk mendeteksi pemotongan pada 24.000 karakter. Parent junction/reparse point ditolak. UTF-8 tidak valid ditolak; BOM UTF-16 tetap didukung. |
| 8C Clipboard Context | Perintah dengan tanda tanya/seru/titik dikenali. Pemeriksaan format clipboard disamakan dengan pembacaan Unicode. |
| 8D Desktop Actions | Pengaturan diperiksa ulang sebelum eksekusi. Konfirmasi yang dibatalkan tidak meninggalkan chat terkunci. Pencarian target mencakup jendela minimized; fokus tidak lagi merestore jendela yang sudah maximized. Handle Process hasil launch dibersihkan. |
| 8E System Context | Nilai scope numerik di luar enum ditolak sebelum pengambilan data. |
| 8F Screen Context | Capture dan encoding berjalan di thread latar belakang. Pembatalan diperiksa sebelum hasil dapat diteruskan. Tetap hanya primary display, satu screenshot per permintaan eksplisit. |

Respons Gemini dinormalisasi untuk tampilan teks biasa sebelum disimpan ke transcript.
Penanda bold/italic, heading, backtick dan escape penekanan umum dibersihkan.
Isi kode, apostrof yang sah dan wildcard path dipertahankan. Instruksi model juga
meminta jawaban teks biasa tanpa escape atau tanda kutip pembungkus yang tidak perlu.
Ini bukan parser Markdown lengkap.

Perbaikan persistensi tambahan: entry memory `null` tidak lagi menyebabkan crash;
file dengan schema versi lebih baru, atau file rusak yang gagal dicadangkan,
dilindungi dari penimpaan oleh perubahan sesi.

## Verifikasi

- `dotnet run --project tests/LuKnight.RenderChecks -- --assistant`: 178 checks lulus setelah seluruh perubahan.
- `dotnet run --no-build --project tests/LuKnight.RenderChecks`: 763 checks lulus; dijalankan sebelum tambahan terakhir perlindungan schema memory (tambahan tersebut diverifikasi ulang oleh suite assistant).
- `dotnet build LuKnight.csproj -c Release --no-restore`: berhasil tanpa warning/error.
- `git diff --check`: tidak menemukan masalah whitespace.

Tes menggunakan fake HTTP Gemini, credential store, snapshot context dan executor
aksi. Tidak ada live API call atau instalasi update. Native cursor/window checks
dilewati oleh suite default; fokus/restore aplikasi nyata, isi clipboard aktual,
capture layar aktual dan analisis gambar oleh Gemini belum diuji end-to-end.
