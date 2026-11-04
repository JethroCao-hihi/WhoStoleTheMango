# Chủ vườn do player điều khiển

Mã dành cho Unity 6, game 3D dùng CharacterController và Input System mới có sẵn trong dự án.

## Chạy thử

1. Chờ Unity biên dịch, mở `Assets/GardenOwnerGenerated/GardenOwnerDemo.unity` bằng cách nhấp đúp asset scene, rồi nhấn Play.
2. Scene và prefab mẫu đã có sẵn trong `Assets/GardenOwnerGenerated`. Có thể tạo thêm scene thử bằng **Tools > Garden Owner > Create Demo Scene** (lưu scene chưa có tên trước khi dùng menu). Menu tạo scene theo chế độ additive để giữ các scene đang làm; cần mở riêng scene demo trước khi Play để tránh camera/collider của scene khác.
3. Nhân vật vàng là chủ vườn; nhân vật xanh là player mục tiêu mẫu đứng yên. Bức tường ở giữa dùng để kiểm tra chó tìm đường vòng. Có thể di chuyển mục tiêu trong Scene view khi Play để xem chó cập nhật đường đi.

| Phím | Hành động |
| --- | --- |
| WASD / phím mũi tên | Đi theo hướng camera |
| Giữ Shift khi di chuyển | Chạy |
| Space | Nhảy khi đang chạm đất |
| Q | Đặt bẫy phía trước, hồi 5 giây |
| E | Gọi một con chó, hồi 15 giây |
| R | Ném dép về phía nhân vật đang nhìn, hồi 1 giây |

Prefab chó, bẫy, dép và nhân vật demo dùng hình khối để chạy thử; thay phần con hiển thị bằng model của bạn. Các thông số đều chỉnh được trong Inspector.

## Gắn vào nhân vật hiện tại

1. Chọn GameObject gốc của nhân vật nông dân trong Hierarchy, chọn **Tools > Garden Owner > Setup Selected Player**. Menu gắn `gardenOwner`, `GardenPlayer`, `CharacterController`, ba prefab và Animator Controller mới. Nhân vật nên có scale gốc `(1, 1, 1)`; chỉnh kích thước/center của CharacterController cho khớp model. Tắt script di chuyển cũ nếu có; tránh Rigidbody động trên cùng nhân vật.
2. Cho mặt đất có collider vào layer **GardenGround**; cho tường/cây/đá cần chặn chó có collider vào **GardenObstacle**. Áp dụng layer cho các object con có collider. Tách hai layer này; không đưa player, chó, bẫy hay dép vào ground/obstacle mask.
3. Chỉnh vị trí **A Star Grid**, `World Size`, `Probe Height` để bao phủ khu vực có thể chơi. Lưới luôn nằm trên trục XZ thế giới, bỏ qua rotation của GameObject. Ground phải nằm trong khoảng `grid.position.y ± Probe Height`. Có thể bật `Draw Grid`, chọn grid và dùng context menu **Rebuild A* Grid** để xem ô xanh/đỏ.
4. Gắn **GardenPlayer vào gốc từng player khác**. Các collider con sẽ tìm về component này. Chó tự loại chủ, player chết và player bị disable; không cần tag `Player`.
5. Camera có tag `MainCamera` được lấy tự động, hoặc gán `Movement Camera`. Có thể tạo điểm ném ở tay và gán `Throw Point`.

## Animator

Controller được tạo tại `Assets/GardenOwnerGenerated/GardenOwner.controller` với ba parameter **Bool**, đúng chữ hoa/thường:

| Parameter | Khi nào bằng true |
| --- | --- |
| `isWalk` | Đang di chuyển trên đất, không giữ chạy |
| `isRun` | Đang di chuyển trên đất và giữ chạy |
| `isJumping` | Đang ở trên không, gồm cả lúc rơi |

Đứng yên trên đất: cả ba bằng false, chuyển về `Idle`. Đi/chạy bị chặn bởi tường thì không phát animation đi/chạy. Script tắt root motion vì CharacterController quyết định vị trí.

Menu gán clip đi/chạy từ hai file `.anim` có sẵn trong `Assets/Sprite`. **Idle và Jump chưa có clip trong dự án**, cần gán clip tương ứng vào hai state này. Các kỹ năng thực hiện ngay khi bấm; chưa có clip động tác đặt bẫy/gọi chó/ném dép. Có thể gọi các hàm kỹ năng qua UI hoặc viết thêm animation event theo nhu cầu.

## Hành vi kỹ năng và A*

- Bẫy chờ 0,5 giây để kích hoạt, gây 10 sát thương và giữ chân 2 giây; tự mất sau 30 giây hoặc sau khi bắt trúng một player khác. Không ảnh hưởng chủ.
- Dép có Rigidbody, trọng lực và kiểm tra va chạm liên tục; gây 20 sát thương rồi mất khi va chạm, tự hủy sau 5 giây. Bỏ qua toàn bộ collider của người ném.
- Mỗi chủ chỉ có tối đa một chó đang tồn tại. Chó sống 20 giây; chủ chết/bị disable/bị hủy thì chó biến mất. Hồi chiêu chỉ bắt đầu khi gọi thành công.
- Chó chọn player gần nhất **có đường đi**, trong bán kính 25; tìm lại mục tiêu và đường đi mỗi 0,5 giây. Không có player khác thì chó đứng chờ; player xuất hiện sau đó vẫn được phát hiện. Mục tiêu ngoài lưới, ô đích bị chặn hoặc không có đường thì chó thử player khác.
- A* dùng `f = g + h`, heuristic octile, 8 hướng; cấm cắt chéo góc vật cản, kiểm tra độ dốc, độ cao bậc và capsule theo kích thước chó. Chó di chuyển bằng CharacterController và cắn gây 10 sát thương/giây khi ở gần; kiểm tra vật cản để không cắn xuyên tường.
- `Agent Radius/Height` của grid phải bằng hoặc lớn hơn kích thước controller chó. `Max Step Height` nên phù hợp `Step Offset` của chó. Lưới chỉ biểu diễn một mặt đất tại mỗi XZ, phù hợp sân vườn/địa hình, chưa hỗ trợ nhiều tầng chồng lên nhau hay nhảy qua hố.
- Lưới mặc định dành cho vật cản tĩnh. Khi đổi vật cản, gọi `navigationGrid.Rebuild()` hoặc đặt `Rebuild Interval > 0`. Với bản đồ lớn, cân nhắc kích thước ô và chu kỳ cập nhật; mỗi trục được giới hạn 200 ô.

## Tích hợp player khác / multiplayer

`GardenPlayer` có máu, `CanMove`, `IsRooted` và các UnityEvent `onDamaged`, `onDied`, `onRooted`. Script `gardenOwner` kiểm tra `CanMove` khi di chuyển/dùng kỹ năng. **Controller riêng của player khác cũng cần kiểm tra `GardenPlayer.CanMove` để hiệu ứng giữ chân có tác dụng lên chuyển động**; nên tiếp tục xử lý trọng lực khi bị giữ chân.

Mã chạy local trong cùng scene, chưa tích hợp framework mạng. Với Netcode/Mirror/Photon, cần xử lý spawn, sát thương và AI trên server, đồng bộ trạng thái về client, và chỉ bật input cho chủ sở hữu nhân vật.

Có thể đặt `readKeyboardInput = false` rồi gọi `Move(Vector2 input, bool run, bool jump)` mỗi frame từ input/controller riêng. Ba kỹ năng có API `TryPlaceTrap()`, `TryCallDog()`, `TryThrowSlipper()` trả `bool`; các thuộc tính `...CooldownRemaining` dùng cho UI hồi chiêu. Các trạng thái `isWalk`, `isRun`, `isJumping` chỉ đọc để luôn phản ánh chuyển động thực tế.
