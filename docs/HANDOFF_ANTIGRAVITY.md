# Latest continuation: build8

Read docs/BUILD8_NOTES.md first. Windows package0.2.0.8 is installed with user-approved machine TrustedPeople signing trust; notification collection is enabled, OS permission Allowed, genuine toast-to-phone banner verified on the current phone client. Build8 IPA adds battery/route styling and dual artwork colors; its new visuals are not physically accepted yet. Prior pending/blocked notification assessments below are historical.

# Current continuation: 2026-11-19

User resumed Codex to preserve the original UI. Read docs/CONTINUATION_2026-11-19.md before the historical handoff below. Build7 adds clock/iPhone battery/inline phone volume/top-only banners on the local UI2 foundation. Remote9c5fdac has a divergent architecture and remains unmerged; no upstream work was deleted. Build7 has no fresh device acceptance.

# BACKTHREAD — BÀN GIAO HIỆN TẠI CHO ANTIGRAVITY

2026-11-18, Asia/Saigon. Người dùng yêu cầu dừng phát triển và bàn giao ngay vì còn khoảng 13% quota. Tiếp tục repo hiện tại; không khởi tạo lại hoặc reset về GitHub cũ.

## 1. Mở đúng repo

- Local: `C:/Users/Duc/Documents/Codex/2026-11-17/files-pasted-by-the-user-backthread/outputs/BackThread`
- Remote: https://github.com/trungduc22/BackThread
- Khi bắt đầu đã fetch: HEAD mới nhất là `6b986ce46902a8e428f852541f38549d940e4c3c`. Mission UI2 được commit local cùng file này, **chưa push**. Chạy `git log -3 --oneline` và `git status --short` trước khi tiếp tục.
- Nếu chuyển máy, dùng Source ZIP của gói bàn giao. SDK, key, pairing record và cấu hình riêng không nằm trong ZIP.
- Handoff cũ được giữ ở `docs/HANDOFF_BEFORE_UI2.md`. Nội dung dưới đây mới là trạng thái hiện hành.

## 2. Yêu cầu MỚI NHẤT — CHƯA IMPLEMENT

Sau khi thấy UI2 chạy thật, người dùng đổi yêu cầu:

1. **Thông báo chỉ là một bar ở phía trên, không toàn màn hình nữa**, kể cả critical. Phải chép thông báo Windows thật, không chỉ gửi fixture qua API. Thiết kế fullscreen hiện tại bị yêu cầu mới thay thế.
2. Thêm chút sống động và một kiểu thông báo đặc biệt/khác lạ. Người dùng để ngỏ thiết kế. Gợi ý đã phản hồi: pulse ngắn, chuyển động nhẹ theo trạng thái phát, tôn trọng Reduce Motion; chưa viết code.
3. Hiện **đồng hồ, rất quan trọng**, pin, Bluetooth, Wi-Fi, CPU đang dùng và RAM. Cách hiểu đã thông báo: pin PC và iPhone, CPU/RAM của PC trong một dải gọn. Telemetry này chưa có. Dùng số đo thật, không đoán Bluetooth connected từ radio enabled hoặc audio route.
4. Âm thanh có ba chế độ: **only PC / only phone / cả hai**.
5. Có **thanh âm lượng ngay màn hình phát**, không phải vào Settings.

Chưa sửa code theo năm yêu cầu này. Đừng báo chúng đã có trong IPA hiện tại.

## 3. Đã làm trong mission UI2

- Native UIKit iOS12, landscape667×375, charcoal/aqua, vector icons gốc; không icon font, SF Symbols, emoji controls hoặc WebView.
- Sáu trang giữ state: Media, Apps, Actions, Worker, History, Connection/Settings. Paging có directional/edge arbitration, tránh slider/UIControl. Corner grip44pt, drawer216×344 với hàng44pt, tap/drag dismiss và generation chống completion cũ.
- `BTMediaView` giữ một AVPlayer/AVPlayerLayer; direct HTTPS media hợp lệ, fit/fill, seek, auto-hide, loading/error, PC/phone ownership. Handoff reply sau actual playing/failed,12s timeout; đổi nguồn hủy request cũ. Direct video chưa test trên phone.
- Coordinator notification: history64/queue8, dedup, expiry, privacy, source/category policy, restore player. Build5 vẫn có critical fullscreen: **cần đổi thành bar theo mục2**.
- Apps/Actions grid reusable, bounded icons/cache, reorder; command ack thật và10s unknown-result deadline; Actions saved order reapplied.
- Windows catalog và tray pinning GUI: exe/Start Menu shortcut/packaged IDs; icon thật từ Windows; canonical path/reparse checks; arguments cố định local; phone chỉ gửi ID. Packaged activation mới compile, chưa runtime acceptance.
- USB/simulator/TLS transport abstraction; iOS SecureTransport TLS1.2 ECDHE-RSA AES-GCM, exact leaf pin constant-time, USB enrollment + fresh session + HMAC, TTL/rate limit/private interface/one owner. USB ưu tiên khi bật fallback. Wi-Fi PCM tắt.
- Permission workflow báo đúng thiếu package identity/capability. Không âm thầm bật quyền.
- `/v1/ui` authenticated own-view inspection/capture. Đã sửa JSON response rỗng bằng explicit WriteAsJsonAsync và thêm regression test. Fixture launcher test xuất file atomic, tránh đọc khi process còn ghi.

## 4. Kết quả thật và giới hạn

| Kiểm tra | Kết quả |
|---|---|
| Windows managed |33/33 pass, có real fixture process launch và TLS/pin/HMAC/reject/expiry tests |
| Native C |wire/PCM ring pass;27 production shared UI decisions pass |
| Python |22/22 artifact/device-tool/analyzer-tool tests |
| Native build |13 production units, ARM64/min iOS12.0/SDK12.4/warnings-as-errors; CoreMedia đã link |
| Analyzer/artifacts |16 units clean gồm probe;9 artifact integrity/scope tests pass |
| Portable |self-contained Host/Simulator API/files/SSE/restart smoke pass |
| Phone protocol |13/13; hello thật có apps/ui-inspect/temporary-wifi-tls |
| Phone UI |40/40 inspection assertions,10 SHA-verified own-view PNG,667×375; user thấy UI mới |
| Phone PCM |24/24, tone3s gain0.15,301frames, Speaker/noErr/nonzero rendered data; sampled gaps/drop/silence0; sender drops0 |
| Audibility bản mới |Chưa hỏi/nhận xác nhận nghe cho tone3s này. Không suy ra nghe được từ counters |
| RSS |protocol23,080,960–23,146,496 bytes; PCM sau mở pages24,252,416–24,498,176 bytes; không phải video/leak/30-minute profile |
| Wi-Fi thật |Phone ack USB enrollment; không đạt TLS ownership trong25s; FAIL, đã revoke, USB còn hoạt động |
| Windows toast thật |Chưa chạy được: portable Host thiếu package identity/userNotificationListener capability |

Reports: `docs/reports/ui2-*`. Ảnh thật: `docs/screenshots/ui2/`. Có media hiện tại/drawer/Apps rỗng/Actions/Worker/History/Connection/banner/critical/restore. Không phải source-layout SVG. Notification text là QA fixture; artwork/media là PC state thật.

Chưa nghiệm thu: finger gestures/FPS/VoiceOver/large text; populated app grid và phone tap-to-launch; native video hidden/visible/loading/seek; real Windows toast; real Wi-Fi;30-minute audio/load/reconnect/interruptions.

**Giữ thất bại lịch sử:** simulator120s cuối vẫn FAIL với9,600 silence bytes, dù sender drops/gaps/overflow0. Windows capture từng có17,416 zero-fill bytes chưa khoanh vùng. WASAPI60s được user nghe liền mạch ở native0.1.2 là bằng chứng cũ, không phải test routing/UI2. Không hạ ngưỡng hoặc tăng buffer để che lỗi.

## 5. Phone/installer/trạng thái cuối

- iPhone6/iPhone7,2, ARM64/iOS12.5.8/1GB RAM. Build hiện tại **0.2.0, CFBundleVersion5**.
- Paired Installation Proxy thử hai lần trả `InvalidService`; không uninstall app, không sửa service. User tự cài IPA theo cách riêng, sau đó Agent/UI mới chạy. Không quan sát cách cài; đừng nói installer đã thành công.
- User nhắc môi trường bypass/mobileactivationd bị thiếu thành phần và dặn không đụng sâu system. Không sửa activation, jailbreak, firmware, execution policy; không reboot/respring/restore để chữa project.
- Port44 có Dropbear nhưng mission này không đăng nhập SSH. Không có SSH credential trong handoff.
- User bỏ yêu cầu web. Site chưa deploy, quyền public đã thu hồi về owner-only, preview server đã dừng. Không tiếp tục website.
- Host đang chạy: `artifacts/ui2/Windows/BackThread.Host.exe`; PID ở `artifacts/ui2/host.pid`; config riêng `artifacts/ios-private/ui2-control-host.json`. Trạng thái cuối: **USB connected, Wi-Fi Off, streamAudio false, notification collection false**. Control-only phục vụ QA; không phải lỗi mất tiếng.
- Trước khi dừng Host, kiểm PID và executable path. Không kill mọi process cùng tên. Không chạy runner USB độc quyền đồng thời với Host.
- Key/config/pairing chỉ nằm trong ignored artifacts/ios-private. Không in, commit, đóng ZIP hoặc thay key để chữa kết nối.

## 6. Build/run và file cần dùng

- IPA: `artifacts/ui2/ios/BackThread.ipa`; UI/Agent0.2.0 DEB và `build-manifest.json` cùng thư mục. IPA chuẩn Payload/ad-hoc integrity; vẫn cần cách ký/cài được chuẩn bị trên phone. Standalone Agent chưa nghiệm thu execution; không chạy song song embedded Agent.
- Windows self-contained: `artifacts/ui2/Windows/`. Không publish đè DLL đang loaded.
- .NET8 local: workspace `work/dotnet/dotnet.exe`, hai cấp trên repo. Set DOTNET_ROOT và DOTNET_CLI_HOME=`work/dotnet-home`; .NET10 hệ thống không thay runtime8 cho app framework-dependent.
- Python thật: `C:/Users/Duc/AppData/Local/Python/pythoncore-3.15-64/python.exe`.
- Zig: `artifacts/ios-toolchain/zig/ziglang/zig.exe`; SDK:`artifacts/ios-toolchain/sdk/iPhoneOS12.4.sdk`; signer:`artifacts/ios-toolchain/codesign/apple-codesign-0.29.0-x86_64-pc-windows-msvc/rcodesign.exe`.
- LLD:`C:/Users/Duc/.rustup/toolchains/stable-x86_64-pc-windows-gnu/lib/rustlib/x86_64-pc-windows-gnu/bin/rust-lld.exe`.
- Native build: `scripts/build-ios.py --linker <lld> --zig <zig> --sdk <sdk> --codesign <signer> --out artifacts/ui2/ios`.
- IPA only: `scripts/install-ios.py --build-dir artifacts/ui2/ios`. `--install` chỉ khi service hợp lệ; không uninstall để chữa lỗi.
- Set `BACKTHREAD_IOS_BUILD_DIR=artifacts/ui2/ios` trước `tests/ios_build_test.py`. Analyzer: `scripts/analyze-ios.py --zig ... --sdk ...`.
- Managed/native: `scripts/test.ps1 -Dotnet <dotnet8>`; executable suite, không phải dotnet-test discovery. Publish đầy đủ: `scripts/build.ps1 -Publish -Output artifacts/ui2/Windows`.
- UI thật: `python scripts/test-native-ui.py --config artifacts/ios-private/ui2-control-host.json --output artifacts/ui2/native-ui --wait-seconds 45`. Host control-only phải chạy; phone foreground. Có bounded retry, không giả capture khi app chưa active.
- Protocol/tone: dừng own Host trước; `BackThread.DeviceTests.exe --key-file <existing-local-key> --fixture --seconds 3 --gain 0.15 --output <report>`. Không audio nếu bỏ fixture/loopback; fixture giới hạn5s.
- Wi-Fi: `python scripts/test-temporary-wifi.py --config <private-config> --address <PC-private-Wi-Fi-IP> --output <report>`. Test luôn revoke. Không tự đổi firewall/mạng trường.

## 7. Audit và thứ tự làm tiếp

1. Tiếp tục đúng yêu cầu mới ở mục2. Root nằm ở `BTViewController.m`; media/navigation/notification/theme/pages đã tách file. Windows: HostRuntime/LocalApi/NotificationAccess/AppCatalog; Transport/TemporaryWifiTransport; Tray AppsForm/WifiForm.
2. **Audit concern chưa sửa/chưa reproduce:** `BTAgent.setNativePlaybackActive` đổi flag rồi stop audio ngoài synchronized block; `audio.start` kiểm ownership trước receiver.start. Serialize toàn bộ ownership/start trước khi thêm PC/phone/both để tránh race PCM start sau AVPlayer.
3. Phone-only phải chứng minh thật: mute PC có thể ảnh hưởng capture theo driver; đo actual capture/receiver. Dùng supported endpoint APIs, giữ/khôi phục original endpoint/mute khi thoát/lỗi. Không tự đổi output device ngoài hành động user chọn mode.
4. PC endpoint volume, phone system volume, AudioQueue gain và AVPlayer volume là các tầng khác nhau. Slider phải nói rõ tầng, phản ánh actual ack. Wi-Fi PCM vẫn disabled; không hứa Windows sound qua Wi-Fi.
5. Clock/telemetry timer nhẹ và main-threadUI. Không private iOS APIs hoặc đọc system files. UI thực tế cần sửa IP placeholder quá tối; kiểm Settings partial row/dots và chỗ cho HUD/volume.
6. Windows toast cần supported package identity/capability + user consent. Chưa có MSIX solution. Không âm thầm cài trust cert/developer-mode hoặc đọc notification database để lách.
7. Wi-Fi physical timeout chưa rõ firewall/network/TLS; không kết luận chắc một nguyên nhân. Windows TLS key dùng temporary current-user crypto container do Schannel không nhận generated ephemeral key trực tiếp; không trust-store write; crash cleanup chưa chứng minh. Không gọi là pure-memory key.
8. Build IPA tiếp theo với build number mới; regression rồi test/capture thật. Cập nhật FEATURE_STATUS/HARDWARE_TEST_RESULTS/NEXT_STEPS. Giữ local commits và strict quality gates. **Không push GitHub nếu user chưa cho phép cho mission này.**
