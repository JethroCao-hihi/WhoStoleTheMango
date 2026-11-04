# Kết quả kiểm thử — 30/10/2026

- **Biên dịch toàn bộ gameplay/runtime:** 64 source file, 0 lỗi.
- **Biên dịch Editor:** 5 source file, 0 lỗi.
- Một warning CS0169 từ Benchmark01 của ví dụ TextMesh Pro có sẵn.
- **Unity 6000.3.8f1 headless: 26/26 assertion pass**, thêm kiểm tra editor setup và controller tạo đủ 8 state/5 parameter, gọi lại không tạo controller mới.

Các hành vi đã chạy bằng engine Unity thật, trong project riêng:

1. Tìm xoài, đi bằng A*, hái ít nhất hai quả, inventory tăng và stock cây giảm.
2. Trói tạm ngừng hái; kho đầy không tiêu thụ quả.
3. Chủ vườn quay lưng: chọn chỗ có che khuất và không nhầm trạng thái gần với đã phát hiện.
4. Chủ vườn biến mất: chờ bộ nhớ nguy hiểm rồi rời Hide.
5. FOV/LOS có tích lũy thời gian xác nhận phát hiện, chuyển Flee và tăng khoảng cách.
6. Chủ vườn biến mất khi đang Flee: AI không kẹt state.
7. Chủ vườn thêm vào scene được kết nối với grid/mask demo.
8. Chó chọn Target: AI tìm cây, bắt đầu leo liên tục và chuyển quyền khỏi CharacterController.
9. Trói trong lúc leo dừng vị trí, không teleport.
10. Đến TreeHide ngoài khoảng cách cắn 3D, cây từ chối reservation không hợp lệ.
11. Lệnh hủy đi bộ không đẩy nhân vật đang trên cây vào Idle giữa không trung.
12. Có CharacterController chiếm điểm đáp: hoãn xuống; hết nguy hiểm và điểm đáp trống: xuống liên tục, bật lại controller trên mặt đất.
13. Đợt truy đuổi mới có thể leo lại; hủy cây trong lúc chó vẫn gần: xuống theo các anchor đã lưu, không lặp leo lên cây đã mất.
14. Hết HP dừng nhiệm vụ và navigation.

Scene demo đã lưu **309 object/component**, bao gồm 4 MangoTree, 4 ClimbableTree và 8 ThiefHideSpot. Vườn được xuất bằng chính ThiefDemoEnvironment trong Unity rồi gắn vào scene hiện có; các tham chiếu local đã kiểm tra đầy đủ, MonoScript trỏ về source của dự án. Giữ các object/camera/mặt đất cũ của demo, không đưa script kiểm thử vào scene.

Bộ test dùng cùng script gameplay và cùng mã dựng vườn, không chạy thao tác bàn phím/clip/rig của nhân vật bạn nhập. Chạy lại qua Tools/ThiefAIValidation/Run.ps1; mã fixture và kết quả tóm tắt được lưu ở Tools/ThiefAIValidation.

Trong project headless tối giản có một exception ở UnityEditor.Search.SearchDatabase khi khởi tạo search index; stack trace không đi qua code gameplay, bộ test vẫn hoàn tất 26 assertion và process thoát với mã 0. Các lỗi UPM/licensing từng gặp khi chạy trong sandbox đã được vượt qua bằng lần chạy headless có quyền truy cập IPC/cache bình thường. Không sửa cache/cấu hình hoặc scene của Editor đang mở.
