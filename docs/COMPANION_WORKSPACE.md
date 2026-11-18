# BackThread — màn phụ có công việc riêng

## Hướng đã chốt với người dùng — 2026-11-19

BackThread vẫn mở trên iPhone 6. Không yêu cầu cài ứng dụng YouTube, Messenger hay Zalo. Giữ giao diện UIKit hiện tại; không thay bằng dashboard web. Màn hình có thể chia hai vùng như điện thoại gập ở tư thế chữ L: nội dung chính trên, công cụ theo ngữ cảnh dưới. Đây là cách bố trí trên một màn hình phẳng, không phụ thuộc cảm biến gập.

Phân biệt hai lợi ích: **chuyển việc sang iPhone** (tải/phát media, nhận mail khi đã có tài khoản), và **chuyển chỗ hiển thị** (log build, trạng thái Git, console của chương trình Windows). Hiển thị console trên iPhone không chuyển quá trình compile Windows sang iOS.

## Các chế độ đáng làm

| Chế độ | Nội dung chính | Vùng công cụ | Hiện trạng |
|---|---|---|---|
| Media | Video do iPhone tự tải/phát native | Queue, play/pause, seek, volume, đổi nguồn | AVPlayer có; signed CDN URL/SSH inbox có trong build10; YouTube trên máy thật chưa xác nhận |
| Build / Run | Log stdout/stderr thật từ tác vụ đã chọn trên PC | Thời gian, mã thoát, build/test/run lại, nhảy tới lỗi | Đề xuất tiếp theo; chưa có terminal/log viewer |
| SSH | Kết quả của tác vụ SSH đã cấu hình | Chọn máy/profile, reconnect, dừng đúng tác vụ | SSH utility hiện có vài lệnh chẩn đoán; inbox build10 chỉ điều khiển media của BackThread |
| Login / Mail | Thư mới và mã đăng nhập nổi bật | Tên nguồn, thời điểm, hết hạn, ẩn mã | Chưa có kết nối mail/OTP trực tiếp; cần đăng nhập từng tài khoản |
| Notifications | Banner ngắn, hàng đợi nhóm theo nguồn | Không làm phiền, mở chi tiết, bỏ qua | Windows mirroring đã xác nhận; nguồn trực tiếp trên phone chưa có |
| Jobs | Tiến độ tải, hash, xử lý file của BackThread | Hủy, retry, kết quả, dung lượng | Có worker giới hạn; không phải shell tùy ý |
| Dev server | Trạng thái server đang chạy và request gần đây | Restart tác vụ được cho phép, cổng, mã HTTP | Đề xuất; cần nguồn sự kiện rõ ràng |
| Git / CI | Branch, file thay đổi, kết quả pipeline | Xem diff/tóm tắt lỗi; thao tác ghi phải rõ mục tiêu | Đề xuất, chưa kết nối repository/CI |
| System | CPU/RAM PC, bộ nhớ app và trạng thái nhiệt iPhone | Cảnh báo nghẽn, đứng hình, mất kết nối | PC metrics đã có; chẩn đoán native media bổ sung trong build10 |
| Quick tools | Nút build/test/run, timer, ghi chú ngắn | Chuyển theo project đang dùng | Có Quick Actions theo ID; project context/timer/notes chưa làm |
| Automations | Công việc theo ngày/tuần và kết quả lần gần nhất | Chạy ngay, tạm dừng, retry, nơi thực thi | Thiết kế dưới đây; chưa có scheduler/provider tự động |

## Automations — đề xuất mới nhất

Người dùng đề xuất iPhone chạy tool hằng ngày: tìm kiếm/lấy ba kết quả đầu, nhận game miễn phí Epic/Steam theo kỳ, gửi video giữ chuỗi TikTok và các tác vụ tương tự. Không coi đây là tài khoản hoặc đích gửi đã được cấu hình; chưa lên lịch hay đăng nhập/gửi/nhận thay người dùng.

Một job nên có: ID cố định, nơi thực thi (phone hoặc PC), múi giờ/lịch, tài khoản đã liên kết, lần chạy tới, thời hạn, giới hạn số lần thử, nhật ký rút gọn và kết quả thực tế. Trạng thái: chờ → đang chạy → hoàn tất / cần đăng nhập / cần thao tác / thất bại. Một lease và khóa theo job+ngày/tuần ngăn cả PC và phone chạy trùng. Mất điện/quá lịch chỉ catch-up tối đa một lần theo cấu hình; không gửi bù một loạt hành động.

| Ý tưởng | Bản hữu ích đầu tiên | Điều kiện để gọi là tự động đầy đủ |
|---|---|---|
| Tìm kiếm / Edge | Nhập truy vấn, lấy ba kết quả từ nguồn hỗ trợ hoặc điều khiển Edge trên PC | Phân biệt lấy kết quả bằng mạng trên phone và mở trình duyệt PC; SSH không biến iOS thành Edge desktop |
| Epic free games | Theo dõi đợt game miễn phí, báo hạn, mở trang game | Nhận game là hành động tài khoản riêng: xác nhận game đúng, giá cuối bằng0, license đã vào thư viện; không nhầm mở trang với nhận thành công |
| Steam freebies | Theo dõi game miễn phí và phân biệt free-to-keep/free-weekend/demo | Chỉ nhận khi integration hỗ trợ tài khoản thật và xác nhận quyền sở hữu; không gọi trial là game giữ vĩnh viễn |
| TikTok streak | Danh sách video đã chọn, đích nhận, nút gửi và nhắc việc | Cần cách gửi hoạt động, tài khoản/đích/lịch được người dùng đặt; lưu ID kết quả để không gửi trùng, dừng khi cần đăng nhập/2FA |
| Mail / OTP | Nhận mail theo từng tài khoản, báo mã ngay trên phone | Đăng nhập và lưu token an toàn trên máy đã được kiểm tra; không trích cookie PC hay đọc dữ liệu app khác |
| Theo dõi link/server | Kiểm tra trạng thái hoặc thay đổi nội dung, báo khi khác | Dùng request nhẹ trên phone khi nguồn cho phép; timeout/cache/backoff để không polling liên tục |
| Tải / checksum | Tải file được chọn về vùng BackThread, xác minh hash, báo xong | Đủ dung lượng, quota, resume và không tự thực thi file tải về |
| Build / test / backup | Chạy tác vụ được cấu hình rồi xem log/kết quả trên phone | Chạy đúng nơi có toolchain; tách log hiển thị khỏi việc chuyển tải tính toán |

Thiết kế hai phần: worker phone cho tác vụ mạng/file nhẹ; worker PC cho trình duyệt desktop và chương trình Windows. UI luôn ghi nơi thực thi để người dùng biết việc nào thực sự chia tải. Không thêm Chromium/Edge hoặc daemon vào hệ thống iPhone để biến tác vụ chưa hỗ trợ thành "đã chạy". Khi app ngủ/đóng, lịch trên phone phải báo không được bảo đảm; dùng PC làm scheduler dự phòng cần PC bật và không phải tự chủ hoàn toàn.

Thứ tự nên thử: một job kiểm tra link không cần tài khoản → một nguồn theo dõi game miễn phí → log build thật → mail đã đăng nhập → hành động tài khoản có xác nhận kết quả. Không bật nhiều job thường trực trước khi đo RAM/nhiệt và khả năng reconnect trên iPhone6.

## Thứ tự triển khai

1. Chứng minh một video H.264/AAC từ nguồn mạng được iPhone tải và hiển thị bằng AVPlayer, không gửi PCM/video từ PC. Đo thời gian tăng, access-log bytes, stalls, codec, RAM và thermal state. Tách việc lấy URL khỏi việc chuyển tiếp video. Chưa được gọi resolver chạy trên PC là tự chủ hoàn toàn. Chỉ đo giảm tải CPU khi nguồn PC đã ngừng giải mã; ngừng gửi PCM không tự dừng tab YouTube trên PC. Chế độ only-phone cần xác nhận phone đã phát rồi mới pause đúng nguồn PC, chế độ đôi phải giữ lựa chọn riêng.
2. Build / Run Deck: một nguồn log thật, bộ đệm hữu hạn, rõ tên tác vụ/working directory/mã thoát; reconnect không chạy lặp tác vụ. Cửa sổ PC chỉ được ẩn khi đã xác nhận đầu ra trên iPhone. Các chương trình cần nhập liệu tương tác phải báo chưa hỗ trợ cho đến khi có PTY đúng nghĩa. Không giả vờ TextView là một terminal.
3. Bố cục hai vùng theo ngữ cảnh, dùng lại màu artwork và chuyển màu hiện tại; không phá drawer/sáu trang. Cho phép chuyển lại full media. Cần kiểm tra độ đọc trên màn hình 667×375 trước khi thêm nhiều nút.
4. Mail + mã đăng nhập: adapter từng nhà cung cấp, đăng nhập rõ ràng, worker phía phone khi runtime cho phép. Không lấy cookie từ browser PC. Mã chỉ hiện lớn sau khi bật riêng, không vào log/history thông thường, tự ẩn/hết hạn; không tự gửi mã sang PC.
5. Mở rộng Git/CI/dev-server sau khi cơ chế job/log/reconnect ổn định. Đo RAM trước khi thêm feed hoặc animation thường trực.

## Ranh giới thực tế

- SSH là đường điều khiển tiến trình/file, không tự mang lại quyền đọc Gmail, Messenger hay Zalo. Gmail và Outlook có xác thực OAuth; iCloud có cơ chế mật khẩu ứng dụng khi tài khoản cho phép. Cần đăng nhập mỗi tài khoản được chọn.
- Messenger Platform phục vụ Page/Professional và Zalo OA API phục vụ Official Account. Các API này không chứng minh quyền truy cập inbox cá nhân. Trên phone hiện chưa có app/phiên đăng nhập hoạt động; nguồn tin cá nhân vẫn bị chặn, không giả lập thông báo để báo thành công.
- Worker trong BackThread không có bảo đảm sống khi app bị đóng/suspend. Không chạy silent audio để giữ mail/SSH sống, không cài daemon hay sửa hệ thống bypass để đạt tính năng.
- Không kết luận nóng do software decoding khi chưa đo. Codec H.264 + AVPlayer là lựa chọn để thử; access log/thermal state không chứng minh phần cứng decoder đang hoạt động hay máy chắc chắn mát hơn.

## Tài liệu nguồn đã kiểm tra

- [Gmail XOAUTH2](https://developers.google.com/workspace/gmail/imap/xoauth2-protocol)
- [Outlook/Exchange IMAP OAuth](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth)
- [iCloud app-specific passwords](https://support.apple.com/en-us/102654)
- [Messenger Platform overview](https://developers.facebook.com/documentation/business-messaging/messenger-platform/overview)
- [Zalo Developers](https://developers.zalo.me/docs/)
- [Apple background execution](https://developer.apple.com/documentation/backgroundtasks/choosing-background-strategies-for-your-app)
- [yt-dlp EJS requirements](https://github.com/yt-dlp/yt-dlp/wiki/EJS)
