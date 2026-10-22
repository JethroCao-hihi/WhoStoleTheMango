# UGS Backend Smoke Test — Hướng dẫn tiếng Việt

## Mục đích

Script này chỉ kiểm thử Unity Authentication + Cloud Save. Nó **không thay thế** `AuthManager.cs`, `SaveManager.cs` hoặc `LoginUIController.cs` của nhánh `feature/auth-and-save`.

## Trước khi bắt đầu

- Project đã được liên kết với Unity Dashboard.
- Package Authentication và Cloud Save đã cài.
- Provider Username & Password đã bật.
- Mở Dashboard > Products > Cloud Save và kiểm tra dịch vụ/Environment đang dùng.
- Chỉ dùng thông tin tài khoản thử nghiệm; không dùng mật khẩu cá nhân.

## Cài đặt an toàn

1. Đóng Unity trước khi chép file nếu cần.
2. Chép `UGSBackendSmokeTest.cs` vào `Assets/_UGSTests/` (tạo folder nếu chưa có). Để Unity tự sinh `.meta`.
3. Mở Unity, chờ compile. Nếu Console báo compile error, đừng tiếp tục; lưu ảnh lỗi đầy đủ.
4. Tạo scene mới `Assets/Scenes/UGSBackendSmokeTest.unity` qua File > New Scene và lưu scene. Không thay đổi `LoginScene` gốc.
5. Tạo Empty GameObject tên `UGSBackendSmokeTest` và gắn component cùng tên vào nó.
6. Nhấn Play. Bảng test xuất hiện góc trên bên trái.

## Cách thử

1. Nhấn `Initialize Unity Services`.
2. Dùng username thử nghiệm hợp lệ, ví dụ `MinhTest01`, và mật khẩu thử như `TestGame!2026`.
3. Nhấn `Register new test account`. Nếu thành công, SDK sẽ đăng nhập tài khoản mới; nút panel sẽ hiện Player ID.
4. Đổi Test EXP (ví dụ 17), bấm `Save test value to Cloud Save`.
5. Bấm `Sign out`.
6. Bấm `Sign in existing test account` với đúng tài khoản vừa tạo.
7. Bấm `Load test value from Cloud Save`; giá trị cũ cần xuất hiện dù đã đăng xuất/đăng nhập lại.
8. Vào Unity Dashboard > Player Management > chọn đúng Environment > tìm người chơi theo Player ID > State/Player Data để kiểm tra key `ugs_backend_smoke_test_v1`.

Lưu ý: đăng ký trùng username sẽ thất bại; lần sau dùng Sign in. Cùng username có thể không cùng database nếu Unity Dashboard và Editor đang dùng khác project hoặc environment.

## Nếu lỗi

- `Invalid credentials`: kiểm tra username/password và thử đúng tài khoản đã đăng ký.
- `Username already exists`: đây là tài khoản đã tồn tại; dùng nút Sign in.
- `Services not initialized` hoặc lỗi Project ID: xác nhận project được Link trong `Edit > Project Settings > Services`.
- Cloud Save unauthorized / forbidden: kiểm tra đã đăng nhập thành công và Dashboard đang dùng đúng Project/Environment.
- Compile error: chụp ảnh Console có dòng lỗi đầy đủ, không sửa/xóa file ngẫu nhiên.

Sau khi kiểm thử thành công, giữ script này làm công cụ test tạm thời hoặc xóa khỏi build khi không cần. Bước tiếp theo mới là refactor `AuthManager` và `SaveManager` để gọi UGS, rồi kiểm tra tất cả nơi khác trong gameplay đang dùng các API đó.
