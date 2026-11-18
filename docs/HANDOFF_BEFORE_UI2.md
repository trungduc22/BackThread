# BACKTHREAD — BÀN GIAO CHO ANTIGRAVITY

## Cập nhật sau bàn giao — 2026-11-18 buổi tối

Codex đã fetch lại GitHub: chưa có commit Antigravity mới, chỉ có master307e167. Tiếp tục đầy đủ lịch sử đó tại `6c2fb35`. Build sạch, 28/28 functional. File I/O simulator đã tách khỏi reader bằng hàng đợi serial hai slot; có telemetry source/queue/write/receiver. Soak120s mới: FAIL; xem `reports/resume-validation.json`, không dùng kết quả cũ thay cho kết quả này.

**Máy thật:** WASAPI60s, 32 checks pass, sampled receiver loss0; người dùng nghe và xác nhận “Có tiếng, nghe liền mạch”. RSS10 samples khoảng12.48–12.57MiB. Capture Windows vẫn có17,416 zero-fill bytes chưa khoanh vùng; chưa đạt nghiệm thu30phút hoặc mọi điều kiện tải. Không cài lại native app vì native source không đổi. Không đụng system. Xem mục mới đầu FEATURE_STATUS/HARDWARE_TEST_RESULTS/NEXT_STEPS trước khi đọc ngữ cảnh cũ bên dưới.


Ngày 2026-11-18, Asia/Saigon. Người dùng sắp hết quota và yêu cầu lưu ngữ cảnh để tiếp tục bằng Antigravity.

**Mở repository hiện có, không khởi tạo lại dự án.** File này là ngữ cảnh bàn giao, không chứa SDK, runtime, credentials hay toàn bộ source. Nếu Antigravity ở cùng máy, mở repo dưới đây. Nếu chuyển máy, cần thêm source snapshot được chỉ ra.

Bản portable mới: `artifacts/resume-build/Windows/`. Host mới đang chạy qua `artifacts/ios-private/resume-ready-host.json`, WASAPI thường, app gain0.25; không đổi volume hệ thống. Gói người dùng: `../BackThread-Resume-6c2fb35/`, có Windows/iOS/Source ZIP và SHA256. Đọc RELEASE.json để biết commit chính xác.

## 1. Yêu cầu mới của người dùng

Người dùng muốn iPhone **tự lấy và phát các nguồn được hỗ trợ** như YouTube, Spotify, có thể thêm app khác. Những nguồn không tự phát được mới dùng PCM Windows → USB → iPhone.

Thiết kế cần có hai đường phát:

- **Independent/native:** PC gửi lệnh và định danh nội dung; iPhone lấy dữ liệu qua Internet của chính nó rồi phát.
- **PC PCM:** Windows phát, BackThread capture WASAPI và truyền PCM qua USB.

Cần chọn nguồn, playback owner, trạng thái thật, fallback và chống phát trùng. YouTube/Spotify độc lập **chưa được triển khai hoặc chứng minh hoạt động**. Đây là yêu cầu bổ sung cho loa USB, media/control UI, notifications, Quick Actions, worker, RAM thấp và recovery; giữ nền tảng Windows/simulator hiện có.

## 2. Giới hạn bắt buộc

Target: iPhone 6, ARM64, iOS 12.5.8, RAM khoảng 1 GiB. Người dùng nói system đã bypass và thiếu nhiều thành phần; **không can thiệp sâu system để làm tính năng chạy**.

- Không jailbreak, Activation Lock/bypass, flash/restore firmware, reset, sửa AMFI/trust cache/activation/sandbox/Jetsam.
- Không cài bù/sửa framework, driver, daemon hay thành phần jailbreak/system; không restart dịch vụ hệ thống để chữa thử.
- Chỉ thay đổi BackThread-owned source/app/data, qua phương thức cài hợp lệ đã có. Không xóa dữ liệu cá nhân/repo khác.
- Thiếu khả năng hệ thống thì ghi BLOCKED/UNSUPPORTED và tiếp tục phần độc lập khác.
- Người dùng đã cho phép đẩy repository này lên GitHub; không tự push thay đổi khác ngoài yêu cầu. Không đọc/sửa live SQLite/repository GhostCode.
- Không log/đóng gói password, session key, OAuth token, pairing record hay raw private device metadata.
- Không suy ra phát loa thật từ simulator/callback/route; không hạ ngưỡng test để biến lỗi thành pass.
- Không tạo tone quá to hoặc kéo dài. Không lặp lại các lần nghe nếu không có dữ kiện mới.

## 3. Repo, HEAD và artifacts

Antigravity có thể clone `https://github.com/trungduc22/BackThread.git`, rồi đọc `docs/HANDOFF_ANTIGRAVITY.md`. SDK/runtime, private config và matching key không nằm trên GitHub; nếu dùng máy khác phải chuẩn bị lại theo hướng dẫn, không đăng bí mật lên repo.

Workdir mọi command:

`C:\Users\Duc\Documents\Codex\2026-11-17\files-pasted-by-the-user-backthread\outputs\BackThread`

Checkpoint implementation mới nhất: **6c2fb355423afab5919c90fc0fd2dcde5fb2545d**. Các mục bên dưới mô tả thêm lịch sử trước lần build buổi tối; ưu tiên cập nhật đầu file.

- `9e69033`: real iPhone integration/audio evidence, native recovery, sửa phép đo, báo cáo.
- `363c821`: chuẩn hóa cuối file log; bộ ZIP checkpoint đã tạo ở commit này.
- `79750c2`: writer mạng chạy riêng, synchronous write timeout, paused-caller regression; **26/26 functional tests pass**.

GitHub đích: **https://github.com/trungduc22/BackThread** (private). Origin được thiết lập trong lần đẩy do người dùng yêu cầu. Không quay lại từ 738a497/0e9f88c rồi mất phần sửa mới.

Bộ ZIP Windows/iOS/Simulator/Source/Documentation và SHA256 hiện có:

`C:\Users\Duc\Documents\Codex\2026-11-17\files-pasted-by-the-user-backthread\outputs\BackThread-0.1.2-checkpoint`

**ZIP Windows/Source của bộ này ở 363c821, chưa có dedicated writer 79750c2.** Không nhầm artifact cũ với source mới. Bản publish mới nhất nằm trong repo tại `artifacts/release-0.1.2-writer/Windows/`.

Source snapshot mới nhất, nếu cần chuyển máy:

`C:\Users\Duc\Documents\Codex\2026-11-17\files-pasted-by-the-user-backthread\outputs\BackThread-Source-79750c2.zip`

## 4. Đã đạt gì trên máy thật

**ĐÃ CÓ ÂM THANH THẬT; không quay lại kết luận cũ “Agent chưa từng chạy”.**

1. Full UIKit app + embedded BTAgent chạy trên iPhone; authenticated USB, hello `simulated:false`.
2. Wrong-key rejection, malformed-version recovery, heartbeat, event/dedup và socket mới đã thử.
3. Upload 1 MiB, whole-file SHA-256, exact range và diagnostic memory chạy thật cùng PCM.
4. Sau Upgrade riêng app và người dùng mở lại, UI hiện **Connected by USB**.
5. Fixture PCM 3 giây đạt **27 kiểm tra vật lý**; người dùng trả lời **“Có nghe tiếng”**. Speaker, 48 kHz, input/render peak 4096, app gain 0.25, observed phone volume 1; sampled gaps/overflow/underrun đều 0.
6. Host phát thường, app gain 1: người dùng nghe được âm thanh PC qua iPhone nhưng nói **“Có nhưng bị ngắt/giật”**. Chất lượng liên tục CHƯA đạt.
7. Loa phát bình thường ở app khác. Hai lần “No sound” trước là lịch sử cần giữ. Không quy hết nguyên nhân cho một patch: app build, foreground state và volume cũng khác nhau.
8. Người dùng xác nhận từng rút cáp. USB disappearance lúc đó là rút thật. Sau đó Host lại kết nối được. Chưa có bài cable reconnect hoàn chỉnh được kiểm thử có kiểm soát.

### Dữ liệu chất lượng cần giữ

- WASAPI 90 giây, app gain 0.25/phone volume khoảng 0.105: **472 sender drops, 132 gaps, 1,416,960 overflow bytes, 2,423,040 silence bytes** trong cửa sổ stream. Nonzero PCM đã render; functional pass không phải quality pass.
- 15 RSS samples native: **11,337,728–11,395,072 bytes**, khoảng 10.81–10.87 MiB. Không phải leak certification.
- Một snapshot gần lúc rút cáp: Windows CPU 100%, RAM available 3,585 MiB, paging 0; Host dùng 0.046875 CPU seconds/3.04 wall seconds, RSS 84,193,280 bytes. Không phải steady-state benchmark hay chứng minh nguyên nhân duy nhất.
- Bản batching trước dedicated writer: live Host có **741 drops sau 11,269 frames**, max write wall time **33.727 ms**, slow writes >=50ms là 0. Gợi ý nghẽn lịch chạy/hàng đợi trước write, chưa phải kết luận nhân quả hoàn chỉnh.
- **Dedicated writer mới chưa có physical listening/quality result hoặc strict soak. Không tuyên bố đã hết giật.**

## 5. Tiến trình, config và kết nối

Host cũ PID 9140 đã được dừng để build writer. Chưa chủ động chạy Host mới trong bước bàn giao. Kiểm tra `Get-Process BackThread.Host` trước khi chạy; không tin PID cũ.

Bản self-contained mới đã publish thành công: `artifacts/release-0.1.2-writer/Windows/`. Giữ DLL/runtime cùng executable. Chỉ một Host/Agent sở hữu cổng.

Config riêng có matching key, USB, `streamAudio=true`, `fixtureAudio=false`, `audioGain=1`, API port 23462:

`artifacts/ios-private/resumed-host.json`

```powershell
& .\artifacts\release-0.1.2-writer\Windows\BackThread.Host.exe `
  --config .\artifacts\ios-private\resumed-host.json
```

Private local files — không in nội dung vào prompt/log/archive:

- `artifacts/ios-private/device-session.key`: HMAC key của BackThread, **không phải SSH private key**.
- `artifacts/ios-private/ssh-proxy-known-hosts`: isolated known-hosts, alias backthread-device.
- `artifacts/ios-private/ui-app.json`: metadata/container thực, có thể đổi khi Upgrade.

SSH từng có trên USB device port 44, username root. Credentials người dùng từng cung cấp riêng không được chép vào file này; SSH không cần cho phát bình thường. Không đoán/in password. Đừng dùng session.key làm identity SSH.

Ports: Apple Mobile Device Service PC 27015; iPhone Agent loopback 23451. API mặc định 23452, config chẩn đoán dùng 23462. PCM không cần port forward/Wi-Fi/Internet. **Phát độc lập cần Internet phía phone**, chưa xác minh; deviceHasInternet mặc định false.

Waiting for USB host không tự chứng minh thiếu pair. Paired TLS đã xác thực. App có thể suspend nếu không có active audio khi chuyển app/khóa màn hình; có thể cần mở BackThread lại. Không ép background bằng system changes. `uicache -l` là query metadata; `uiopen` có thể có dialog Open.

## 6. Source cần đọc

- `src/Windows/Transport/Connection.cs`: NetworkStream writer LongRunning riêng, AboveNormal, write timeout 3 giây; generic Stream giữ async fallback. Queues bounded, control ưu tiên, audio drop-oldest.
- `src/Windows/Transport/Protocol.cs`: pooled header+payload, wire bytes không đổi; sync/async dùng cùng encoder; buffer trả sau write.
- `tests/BackThread.Tests`: golden header, async pooled lifetime, paused caller/context test; 26/26 pass không có nghĩa hết starvation ở mọi load.
- `src/Windows/Host/HostRuntime.cs`: audio init/volume rejection và unsolicited audio.error không cố ý đóng control/jobs; heartbeat/reader vẫn hoạt động. Status có audioError, audioWriteMaximumMilliseconds, audioSlowWrites. Write wall time gồm local scheduling.
- `HostConfig.cs`: audioGain 0..1, default 1; chỉ app gain, không phone system volume. DeviceTests quiet gain riêng <=0.3.
- `src/iOS/AudioReceiver/BTAudioReceiver.m`: reset `_requested` trong lifecycle lock với Stop; volume/getter errors; queue state/presence; peaks/nonzero counts.
- `src/iOS/Agent/BTAgent.m`: queue lỗi chỉ dừng/discard PCM, giữ USB; own `.network-diagnostics.json` có phase/errno/port/PID.
- `tests/BackThread.DeviceTests/AudioMeasurementWindow.cs`: deadline từ pacing, absolute polls, bỏ late replies/shutdown tail; stats chậm không kéo dài tone.
- `src/Windows/Worker/RemoteWorker.cs`: validate range offset/count/declared length, bốn download slots, corrupt-response recovery.
- `scripts/analyze-ios.py`: fresh run ID, complete/passed/expected count; không reuse old clean report sau incomplete run.

### Independent playback phải xử lý các chỗ này

- `src/Windows/Media/Handoff.cs`: DirectMediaProvider chỉ nhận HTTPS media URL được hỗ trợ, cần deviceHasInternet, từ chối YouTube page URL.
- `HostRuntime.HandoffAsync`, `MediaLoopAsync`, `AudioLoopAsync`.
- `src/iOS/UI/BTViewController.m`: media.handoff hiện chỉ tạo AVPlayer/play, thiếu result/error handshake và PCM coordination; received:true không phải playing.
- `BTAudioReceiver.stop` có thể deactivate shared AVAudioSession; coordinator phải tránh tắt session của native player khác.
- Agent connection cleanup gọi audio.stop. Native player phải không bị ràng buộc sai vào tuổi thọ USB session.
- Windows media.state mỗi giây đang cập nhật UIKit; không để nó đè UI khi playback owner là iPhone.
- Play/pause/seek phải tới đúng owner, không điều khiển nhầm Windows khi người dùng đang phát native.

## 7. Kiểm chứng và báo cáo

- New writer: **26/26 functional**, `docs/reports/dedicated-writer-tests.json` và `artifacts/continuation/dedicated-writer-tests.txt`.
- Trước writer: 25/25 functional; latest portable Host API/file/SSE/restart smoke pass; isolated portable simulator acceptance 19 checks pass.
- Native: 10 analyzer units clean, 9 artifact checks, 9 device-tool checks, 4 analyzer-tool checks; shared C wire/ring pass.
- Năm native artifacts reproducible ở hai output dirs. IPA release byte-identical với IPA đã cài/nghe được.
- Physical fixture 27 checks + human confirmation; không áp kết quả đó cho writer Windows mới chưa thử.

**Quality gate vẫn thất bại:** strict simulator 120.326 giây sau timing fix/batching nhưng trước dedicated writer: 118 sender drops, 20 gaps, 5,760 overflow, 243,840 silence; 98 lượt 4-MiB file/hash. Final stats lấy trước Stop nên không thể đổ hết cho shutdown tail. Giữ ngưỡng zero loss.

Có hai soak thất bại khác được giữ lại, và một portable SHA job lỗi “Access to the path is denied”; rerun isolated pass, nguyên nhân IO chưa rõ. Không tắt antivirus/đổi ACL/system policy.

Báo cáo chính:

- `docs/reports/repairs-human-listening.json`
- `repairs-physical-fixture.json`, `repairs-physical-wasapi90.json`
- `pre-dedicated-writer-live-status.json`
- `repairs-soak-final-metrics.json`
- `repairs-validation.json` là snapshot trước dedicated writer, 25 checks.
- FEATURE_STATUS/HARDWARE_TEST_RESULTS/NEXT_STEPS/KNOWN_ISSUES/PERFORMANCE/PROGRESS/REVIEW trong docs.

Continuation/review reports cũ là lịch sử, có blocked-entry trước đột phá; không xóa hay dùng chúng làm trạng thái cuối.

## 8. Build/test tái lập

Windows 10 build19045, i7-5500U 2 cores/4 logical. Không có macOS/Xcode/Theos/WSL dùng được. .NET8 SDK nằm trong task workspace:

```powershell
$env:DOTNET_ROOT=(Resolve-Path '..\..\work\dotnet').Path
$env:DOTNET_CLI_HOME=(Resolve-Path '..\..\work\dotnet-home').Path
& "$env:DOTNET_ROOT\dotnet.exe" build BackThread.sln -c Release --nologo -m:1 -nodeReuse:false
& "$env:DOTNET_ROOT\dotnet.exe" tests/BackThread.Tests/bin/Release/net8.0-windows10.0.19041.0/BackThread.Tests.dll
```

Soak mới cho writer, chạy riêng sau builds/publish, không nén ZIP cùng lúc:

```powershell
$env:BACKTHREAD_TEST_FILTER='soak.concurrent'
$env:BACKTHREAD_SOAK_SECONDS='120'
$env:BACKTHREAD_TEST_REPORT='artifacts/new-writer-soak.json'
& "$env:DOTNET_ROOT\dotnet.exe" tests/BackThread.Tests/bin/Release/net8.0-windows10.0.19041.0/BackThread.Tests.dll
```

Xóa env filter/soak/report trước bài khác. Không test executable cũ rồi nhận là source mới; không build đè DLL đang được Host dùng. Acceptance runner cần độc quyền Agent session, phải nhường Host trước.

Native tools sẵn có:

- Python thực: `C:/Users/Duc/AppData/Local/Python/pythoncore-3.15-64/python.exe` (alias python từng exit không rõ; dùng interpreter thực).
- Zig0.13/Clang18: `artifacts/ios-toolchain/zig/ziglang/zig.exe`.
- SDK12.4: `artifacts/ios-toolchain/sdk/iPhoneOS12.4.sdk`.
- LLD23.1.5: `C:/Users/Duc/.rustup/toolchains/stable-x86_64-pc-windows-gnu/lib/rustlib/x86_64-pc-windows-gnu/bin/rust-lld.exe`.
- Signer: `artifacts/ios-toolchain/codesign/apple-codesign-0.29.0-x86_64-pc-windows-msvc/rcodesign.exe`.

```text
python scripts/build-ios.py --linker <LLD23.1.5.exe> --zig <zig.exe> --sdk <iPhoneOS12.4.sdk> --codesign <rcodesign.exe>
python scripts/analyze-ios.py --zig <zig.exe> --sdk <iPhoneOS12.4.sdk>
python tests/ios_build_test.py
python scripts/install-ios.py
```

Lệnh cuối chỉ tạo IPA; --install mới dùng normal installer để Upgrade riêng app. LLD dùng original multi-document TBDs, min12/SDK12.4, legacy dyld, không chained fixups. Không quay lại normalized-TBD Zig linker cũ. SDK/tools không ship trong ZIP; terms/hash ở IOS_TOOLCHAIN.md.

## 9. Thiết kế hybrid: làm theo capability thật

### Playback owner/coordinator

Owner như PC_PCM / PHONE_DIRECT / PHONE_YOUTUBE / EXTERNAL_SPOTIFY; state Switching/Playing/Paused/Failed, generation bỏ callback cũ. Protocol/capabilities additive, giữ wire v1 nếu không có lý do đổi.

Chuyển nguồn: pause PC source tương ứng, dừng PCM đúng nhánh, chuyển quyền session, start provider, chỉ xác nhận sau state thật. Thất bại phải có reason/fallback. Không giữ queue cũ/phát trùng.

Giai đoạn đầu chuyển mode từng nguồn. Muốn native + các âm PC khác đồng thời phải kiểm thử capture routing, mixing/ducking và background riêng; chưa có bảo đảm.

### Capture và chống double playback

WASAPI hiện thu toàn bộ default multimedia endpoint. PC còn phát nguồn handoff thì PCM vẫn chứa nó. Không thể trừ một app khỏi mix bằng title/metadata.

Microsoft process-loopback sample yêu cầu build20348+, máy hiện19045. Không mặc định dùng API đó ở đây; không thêm virtual audio driver/system routing như lối tắt. Pause/dừng nguồn PC khi handoff trước.

### Direct media trước

Hoàn thiện AVPlayer với HTTPS MP3/AAC/HLS/MP4 được phép dùng, codec thực sự tương thích; có status/error/position/result handshake. Test Internet phía phone. Không disable TLS/ATS toàn cục. Không tải cả video lớn vào RAM; cache hợp lệ phải có quota/retention trong app.

### YouTube

Kiểm chứng official client sẵn có hoặc official embedded player trong WKWebView trên WebKit iOS12 thực tế. User gesture/autoplay/inline/fullscreen/background là gate riêng.

App YouTube hiện App Store ghi yêu cầu iOS17+. Không mặc định app mới chạy trên iOS12; nếu app cũ đã có, kiểm chứng thật, không giả OS/sửa system để ép chạy. Có WKWebView không chứng minh website/login/background hiện còn hỗ trợ.

“Tự kéo” theo nghĩa stream khác lưu offline. Không hứa download/background bằng cách coi page URL là direct AVPlayer URL hoặc xử lý stream/DRM/login bảo vệ ngoài hỗ trợ dịch vụ. Nguồn unsupported phải hiện lý do/fallback.

GSMTC title/artist không đủ khôi phục video URL chính xác. MVP dùng URL người dùng cung cấp hoặc integration có quyền rõ; không đoán từ title/scrape cookies/browser database.

### Spotify

Spotify support hiện yêu cầu app iOS16.1+. iOS App Remote SDK điều khiển Spotify client đã cài, không cấp raw music stream để BackThread tự phát như MP3.

Kiểm tra client hợp lệ đang có có thực sự login/play không; SDK/API/account/OAuth có hỗ trợ không. Không mặc định bản cũ vẫn chạy hoặc tự cài/sửa client ngoài phạm vi BackThread. Nếu unsupported thì blocked và PCM fallback; không đọc dữ liệu đăng nhập app khác.

External Spotify sở hữu audio có thể khiến BackThread suspend. Không hứa worker/USB/UI sống nền vô điều kiện, không tạo daemon/tweak để né lifecycle.

### Providers khác / UI

Capabilities: playable, requiresNetwork/auth/userAction, background/mixing và unavailable reason. Commands tới owner đúng. UI phân biệt YouTube/iPhone với Chrome/PC, không giả thành công. Giữ một WebView/provider đang dùng, unload khi rời; đo lại RSS trên RAM1GiB.

## 10. Làm tiếp theo thứ tự

1. Xem git status/log, dùng đúng79750c2. Xác minh một Host, matching key, app ở trạng thái có thể phục vụ. Không pair/reset lại chỉ vì Waiting.
2. Test writer mới: sender drops, queue residence/write waits, capture-ring silence/drops, receiver arrivals/occupancy và nghe thật. 26 functional checks hay low write time không đủ chứng nhận hết giật.
3. Xác minh phone Internet + khả năng từng provider iOS12 trước UI lớn. Chấp nhận YouTube/Spotify có thể blocked.
4. Coordinator + Direct AVPlayer result/fallback; USB control/worker vẫn sống khi chuyển nguồn hoặc audio lỗi.
5. Provider adapters theo hỗ trợ chính thức; không download/auth/system workaround. Ghi rõ compiled/partial/blocked/unverified.
6. Test chuyển owner, double audio, mất mạng/auth, screen-off, unplug/replug, pause/seek, worker+audio, RSS/crash. Independent playback phải được xác nhận thật.
7. Update docs, commit local, package với hash. Bộ ZIP hiện tại là checkpoint, chưa chứng nhận chất lượng liên tục.

## 11. Nguồn chính thức đã kiểm tra 2026-11-18

- [Spotify supported devices/iOS minimum](https://support.spotify.com/us/article/supported-devices-for-spotify/)
- [Spotify iOS SDK, client + OAuth/App Remote](https://developer.spotify.com/documentation/ios/getting-started)
- [Spotify SDK repository](https://github.com/spotify/ios-sdk)
- [YouTube App Store/current iOS requirement](https://apps.apple.com/us/app/youtube/id544007664)
- [YouTube official embedded player parameters](https://developers.google.com/youtube/player_parameters)
- [Microsoft process loopback minimum build](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)

Các yêu cầu dịch vụ thay đổi; recheck khi triển khai. App/SDK compile không chứng minh server còn hỗ trợ.

---

**Bắt đầu:** repo local mục3, implementation6c2fb35; 28 functional pass; WASAPI60s đã được nghe liền mạch, sampled receiver loss0; simulator soak120s vẫn FAIL với9,600 silence bytes. Chưa có nghiệm thu30phút. Yêu cầu independent providers + PCM fallback còn dang dở trên iOS12. Không đụng system.
