# Unity import: Grow + Ground

Đây là phần bổ sung tùy chọn cho project Unity hiện tại. Gói bao gồm tính năng grow cho hệ Fusion đang có và mã demo Garden Owner lấy từ backup.

## Grow: xoài mọc lại có đồng bộ Fusion

1. Copy thư mục `Scripts` vào `Assets` của project Unity (giữ file `.meta` nếu có).
2. Trên từng GameObject đã có component `MangoTree`, thêm `NetworkMangoRegrowth`.
3. Chỉnh `Regrow Seconds` theo ý muốn (mặc định 20 giây).
4. Giữ `Assets/Prefabs/Mango.prefab` trong Fusion NetworkPrefabTable. Prefab cần có `NetworkObject`, `NetworkTransform`, `NetworkMango` và `NetworkDroppedMangoMarker`.
5. Chạy trận với Host. Host là bên duy nhất spawn quả; client nhận quả qua Fusion. Bộ đếm chỉ chạy trong phase Playing.

`MangoTree.MangoPoints` phải trỏ tới đúng các vị trí quả ban đầu. Component dò slot trống theo vị trí, bỏ qua xoài đã bị đánh dấu là dropped, rồi tạo lại quả sau thời gian chờ. Chỉ bật tính năng này khi gắn component; bỏ component sẽ giữ hành vi hiện tại.

## Garden Owner

Trong `Assets/Script/gardenOwner` có controller chủ vườn, máu/khóa di chuyển, chó truy đuổi A*, bẫy và dép ném. `Assets/GardenOwnerGenerated` chứa prefab, material, animator controller và hai scene demo.

- Có thể mở riêng `GardenOwnerDemo.unity` để xem demo. Đừng mở additive cùng GameScene để chơi vì demo có camera, ground và player riêng.
- Để thêm các script vào project, copy cả `Assets/Script/gardenOwner` và `Assets/GardenOwnerGenerated` vào `Assets` của project Unity, giữ nguyên các file `.meta`.
- `gardenOwner` dùng Input System và điều khiển local bằng bàn phím. Trap, chó, dép và máu hiện dùng `Instantiate`/local state, chưa chạy server authoritative và chưa đồng bộ Fusion. Không gắn trực tiếp lên player network trong GameScene nếu chưa chuyển skill, damage và trạng thái sang Fusion.
- Gói demo không thay thế player controller hoặc role system đang có trong game.

## Ground

Project hiện tại đã có `Assets/Prefabs/Ground.prefab` và material/texture ground trong `Assets/YughuesFreeGroundMaterials`. Dùng prefab đó để đặt mặt đất, hoặc dùng mesh/terrain hiện có; không cần nhập thêm prefab trùng tên từ backup.

Đảm bảo mặt đất có collider để `CharacterController` và tương tác trong game hoạt động. Nếu dùng Ground cho navigation AI chủ vườn trong demo backup, layer và navigation cần cấu hình riêng; các script đó chưa đồng bộ Fusion.

## Ghi chú

Script grow sử dụng các lớp đang có trong project (`MangoTree`, `NetworkMango`, `NetworkDroppedMangoMarker`, Fusion và `NetworkMatchState`). Đây là addon cho cấu trúc hiện tại, không thay thế các lớp đó.
