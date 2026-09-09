# LuKnight - Stabilized Chat Build

Perubahan utama:

- Gemini model diperbarui ke `gemini-3.8-flash`.
- Request memakai REST `v1beta` + header `x-goog-api-key`.
- Timeout 30 detik dan pesan error HTTP yang lebih jelas.
- Conversation history disimpan hingga 20 turn terakhir.
- Input dikunci saat request berjalan agar respons tidak saling tumpang tindih.
- Status chat menunjukkan Local / Ready / Busy / Connected / Error.
- Pesan awal tidak lagi mengklaim AI belum aktif ketika API key tersedia.
- `bin/` dan `obj/` tidak perlu disimpan di source project.

## Menyiapkan API key di Windows

PowerShell / Command Prompt:

```powershell
setx GEMINI_API_KEY "API_KEY_KAMU"
```

Setelah menjalankan `setx`, tutup semua terminal dan VS Code yang lama, lalu buka kembali.

Cek dari PowerShell baru:

```powershell
$env:GEMINI_API_KEY
```

Jangan commit API key ke source code.

## Menjalankan

Dari folder project:

```powershell
dotnet clean
dotnet build
dotnet run
```

Jika status di chat menunjukkan `Local mode`, environment variable belum terbaca oleh process aplikasi.
