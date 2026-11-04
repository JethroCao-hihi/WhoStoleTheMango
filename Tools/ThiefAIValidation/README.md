# Kiểm thử Unity Thief AI

Từ thư mục gốc dự án, sau khi Unity đã import và tạo các file csproj:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ThiefAIValidation/Run.ps1
```

Script tìm Editor/Data từ reference của csproj; có thể truyền `-UnityDataPath` nếu dùng vị trí cài khác. Cần Unity đã được cấp phép và thư mục Library/ScriptAssemblies có Input System/UGUI sau một lần import bình thường.

Bộ kiểm thử tạo/chạy project riêng dưới Temp/ThiefAIValidation/UnityProject, giữ scene đang mở của bạn. Dùng DLL gameplay biên dịch từ source hiện tại, DLL Input System/UGUI của dự án và controller/setup mới; không tải package qua mạng. Unity chạy headless với cửa sổ ẩn, có giới hạn 5 phút.

FullThiefValidationRunner.cs gồm 26 assertion cho nhiệm vụ và các trường hợp biên; ValidationEntry.cs kiểm tra thêm công cụ setup/controller. Không phải kiểm thử hình ảnh, rig hoặc bàn phím của nhân vật thật. Xem LastResult.txt cho kết quả lần chạy đã hoàn tất; full log của lần chạy lại nằm dưới Temp.
