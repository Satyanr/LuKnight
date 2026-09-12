# Phase 6E–6H

## 6E — AI & Chat

Settings → AI & Chat menyediakan pilihan Gemini / Local, model Gemini, bahasa Automatic / Indonesia / English, panjang jawaban Short / Normal / Detailed, gaya Friendly / Professional / Playful, serta riwayat selama sesi.

Masukkan key baru di kotak password, lalu pilih **Update Key**. Key disimpan sebagai generic credential `LuKnight/GeminiApiKey` pada Windows Credential Manager. Kotak input dikosongkan sesudah digunakan; key tersimpan hanya diperlihatkan sebagai indikator tersamarkan. **Remove Key** menghapus credential tersebut. `GEMINI_API_KEY` tetap menjadi fallback developer ketika credential tidak tersedia, dan sumbernya dijelaskan di UI.

**Test Connection** melakukan satu permintaan Gemini singkat tanpa mencampurkannya ke riwayat chat. Status connected hanya muncul setelah respons berhasil. Provider gagal, internet bermasalah, atau key tidak tersedia tidak menghentikan aplikasi: pesan lokal menjelaskan keterbatasannya. Mode lokal bukan model AI offline. Teks error provider tidak ditampilkan mentah agar tidak memantulkan data permintaan/key.

**Clear Conversation** menghapus konteks Gemini dan bubble chat. Mematikan Remember membuang konteks model; bubble yang sudah tampil tetap dapat dibaca sampai Clear atau aplikasi ditutup. Percakapan tidak disimpan ke disk. Perubahan model/provider mengosongkan konteks model agar percakapan lintas-model tidak tercampur. Pengaturan chat dan penghapusan konteks ditahan selama permintaan sedang berjalan.

## 6F — Configuration

Lokasi: `%LocalAppData%\LuKnight\settings.json`.

`Models/AppSettings.cs` memisahkan General, Behavior, Chat, posisi mascot/monitor terakhir, posisi/ukuran Settings, dan waktu pemeriksaan update terakhir. General/Behavior/Chat otomatis tersimpan saat diubah. Tombol **Save** mencoba menyimpan kembali jika terjadi kegagalan. Startup registration tetap memakai HKCU Run; preferensi StartHidden dari fase 6C dimigrasikan ke JSON ketika file konfigurasi pertama dibuat.

`SettingsService` menyediakan Load, Save, Reset, Validate, dan Migrate. Penulisan memakai file `.tmp`, flush ke disk, lalu replace atomik dengan `.bak`. JSON rusak disalin ke `.invalid-<timestamp>.bak` sebelum default digunakan. Jika backup/read gagal atau schema lebih baru dari aplikasi, file lama dipertahankan dan tidak ditimpa. Status penyimpanan ditampilkan di footer Settings; aplikasi tetap dapat dipakai selama sesi saat penyimpanan gagal.

Schema saat ini 1; schema 0 tanpa penanda dimigrasikan dengan default bagian yang belum tersedia. Enum/model dan posisi tidak valid diperiksa saat load. File schema yang lebih baru tidak diturunkan secara paksa. Nilai turunan behavior, key, transcript, state jatuh/tidur, dan lintasan aktif tidak diserialisasi.

Posisi disimpan ketika keluar/restart atau menutup Settings. Mascot kembali ke pijakan desktop pada monitor terakhir, tanpa memulihkan state jatuh atau menggantung. Jika monitor tidak tersedia, posisi dipindah ke monitor yang tersedia. Settings memulihkan posisi/ukuran dan mengembalikan window ke area layar jika posisinya tidak lagi dapat dijangkau.

## 6G — Installer / Packaging

Build final menghasilkan:

- `artifacts/publish/LuKnight.exe`: aplikasi Windows x64 self-contained, dengan runtime .NET dan aset sprite.
- `artifacts/release/LuKnightSetup.exe`: installer per-user tanpa administrator.
- `artifacts/release/update.json` dan `checksum.sha256`: metadata update dan SHA-256 installer yang sama.

```powershell
./tools/Build-Release.ps1 -Version 1.0.0 -Iscc 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

Compiler lokal yang digunakan saat implementasi: Inno Setup 6.2.2 resmi, signature installer tool valid, diekstrak di `.tools` tanpa instalasi tool global. Script juga dapat dipakai dengan Inno Setup 6 pada CI. NuGet cache proyek ada di `.tools/nuget`.

Installer memakai lokasi default `%LocalAppData%\Programs\LuKnight`, Start Menu shortcut, desktop shortcut opsional, opsi launch, serta uninstall. Ikon resmi multi-ukuran berasal dari sprite Lu-Knight dan dipakai EXE, tray, dan installer. Uninstall menghapus entry startup dan shortcut; pengaturan/key dipertahankan secara default. Penghapusan pengaturan/key ditawarkan pada uninstall interaktif. Tutup Lu-Knight melalui tray sebelum uninstall.

Build menggunakan staging bersih di `artifacts`, bukan direktori source. Script memeriksa isi publish dan menolak `.env`, konfigurasi user, credential/secret files, PFX/P12, PDB, file sementara, folder test/tool/reference/output, serta pola key/private key pada file teks. Hanya output publish yang masuk installer.

## 6H — Versioning & Update

Version, AssemblyVersion, FileVersion, dan InformationalVersion ditetapkan di project; parameter build release memperbaruinya bersama-sama. Versi awal paket ini 1.0.0. Feed memakai stable release repository `Satyanr/LuKnight` melalui GitHub API.

Pada startup, pemeriksaan otomatis dilakukan jika pemeriksaan sebelumnya lebih dari 24 jam. Update tersedia memunculkan pemberitahuan tray; pembaruan tidak dipasang otomatis. Pemeriksaan metadata dibatasi waktu dan ukuran. About menyediakan **Check for Updates**, **Download Update**, **Install & Restart**, dan **Later**.

Kontrak release:

```json
{
  "version": "1.0.0",
  "url": "https://github.com/Satyanr/LuKnight/releases/download/v1.0.0/LuKnightSetup.exe",
  "sha256": "<SHA-256 installer, 64 digit hex>",
  "size": 12345678
}
```

Tag harus `v<major>.<minor>.<patch>`, dengan asset bernama `update.json` dan `LuKnightSetup.exe`. Manifest dan installer harus berasal dari jalur release repository yang ditentukan. Draft/prerelease, versi yang tidak lebih baru, metadata tidak valid, ukuran berlebih, dan checksum salah ditolak. Unduhan memakai `.part` dan baru menjadi kandidat installer setelah ukuran dan SHA-256 cocok. Installer diverifikasi lagi tepat sebelum proses dijalankan.

Pemasangan ditahan saat drag, falling, atau permintaan AI aktif dan dibatalkan jika konfigurasi gagal disimpan. Setelah launcher berhasil, aplikasi menyimpan state dan melepas tray; installer menunggu PID lama selesai sebelum mengganti file. Jika installer tidak dapat dimulai, aplikasi tetap berjalan. Jika unduhan/validasi gagal, instalasi saat ini tidak disentuh. Jika wizard dibatalkan, aplikasi lama dapat dibuka kembali. Installer tidak menghapus instalasi lama sebelum tahap penyalinan normalnya.

Single instance memakai named mutex. Pembukaan kedua meminta instance aktif ditampilkan. Restart menunggu proses lama selesai agar tidak terhalang mekanisme single instance.

Workflow `.github/workflows/release.yml` menjalankan tes, membangun paket, mengunggah artifact, dan membuat **draft** GitHub Release saat tag `v*` didorong. Draft harus dipublikasikan setelah review agar terbaca oleh updater. Implementasi ini tidak mendorong tag, membuat release remote, atau memasang aplikasi ke akun pengguna secara otomatis.

## Verification

Build Release bersih dan 587 pemeriksaan lolos, termasuk 53 pemeriksaan service produk dan 42 pemeriksaan Settings.

```powershell
dotnet build LuKnight.csproj -c Release --no-restore
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --product
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --settings
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
```

Tes khusus memakai credential palsu, handler HTTP tiruan, dan launcher installer injeksi. Cakupannya termasuk round-trip konfigurasi, backup/recovery, schema baru, kegagalan save, riwayat chat, preferensi prompt, cancellation, fallback, sumber update, checksum, tampering setelah verifikasi, aktivitas yang menahan update, serta kegagalan peluncuran installer. Render Settings diperiksa offscreen pada ukuran normal dan minimum. Arsip installer juga diuji integritasnya melalui `innoextract -t`.

Belum diuji secara interaktif: koneksi memakai key nyata, penyimpanan credential Windows nyata, login Windows, perubahan monitor/DPI, install/uninstall ke profil pengguna, dan upgrade end-to-end dari GitHub Release publik. Paket belum ditandatangani dengan sertifikat penerbit aplikasi.

Referensi implementasi: [Gemini generateContent](https://ai.google.dev/api/generate-content), [Windows CredWrite](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew), [GitHub Releases API](https://docs.github.com/en/rest/releases/releases), [Inno Setup per-user](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm).
