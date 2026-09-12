# Phase 7A — Assistant Core / Conversation Manager

Alur pesan sekarang: `MainWindow → AssistantController → ChatCoordinator → Gemini / Local`. `AppServices.Assistant` memakai instance `AppServices.Chat` yang sama, sehingga pengaturan provider, model, credential, dan fallback Phase 6 tetap berlaku.

- `Assistant/AssistantModels.cs`: kontrak request/reply, role, backend aktual, timestamp UTC, dan sumber Chat/Tray/Voice/System. Voice hanya kontrak untuk fase mendatang.
- `Assistant/ConversationManager.cs`: transcript sesi maksimal 100 turn di RAM, normalisasi teks, tampilan read-only, snapshot terpisah, dan clear. Tidak ada penulisan transcript ke disk.
- `Assistant/AssistantController.cs`: mencatat request yang diterima, meneruskan ke coordinator, mencatat reply sukses, serta mengembalikan backend aktual dari hasil coordinator.
- `MainWindow`: mengirim `AssistantRequest`, menampilkan `AssistantReply.Text`, memakai backend reply untuk status, dan memeriksa busy melalui Assistant sebelum update.

Clear Conversation dari Settings menghapus transcript Assistant, konteks Gemini, dan bubble chat. Request kosong, pre-cancelled, atau ditolak karena busy tidak menambah transcript. Request yang dibatalkan sesudah diterima menyisakan turn user tanpa membuat reply palsu; busy kembali normal dan request berikutnya dapat dikirim. Clear saat request berjalan tetap mengikuti guard milik coordinator.

Transcript Assistant sengaja terpisah dari konteks Gemini maksimal 20 turn. `RememberConversation` tetap hanya mengatur konteks Gemini; transcript sesi Assistant tidak ikut dihapus. Pemindahan konteks menjadi pekerjaan 7C, dan long-term memory menjadi pekerjaan 7D. Tidak ada perubahan implementasi provider pada 7A.

## Verifikasi

`dotnet clean` dan build Release berhasil tanpa warning/error. Sebanyak 35 pemeriksaan Assistant dan 622 pemeriksaan keseluruhan lolos. `dotnet run -c Debug` juga dijalankan; proses Debug aktif dan log startup tidak memuat error. Ini merupakan pemeriksaan startup proses, bukan verifikasi interaksi desktop atau API nyata.

```powershell
dotnet clean
dotnet build -c Release
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --assistant
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
dotnet run -c Debug
```

Tes khusus menggunakan respons Gemini tiruan tanpa permintaan API nyata. Cakupan: role/source/timestamp, normalisasi, snapshot, cap 100 turn, Gemini/local/error fallback, history Gemini dan Remember yang tetap berlaku, clear kedua konteks, cancellation/retry, busy guard, integrasi kirim MainWindow, serta Clear dari Settings sampai bubble chat. Pengujian interaksi UI bersifat offscreen/sintetis; keberhasilan koneksi Gemini nyata tidak diklaim.
