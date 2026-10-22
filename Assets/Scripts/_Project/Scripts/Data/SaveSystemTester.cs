using UnityEngine;

namespace MangoHeist.Data
{
    /// <summary>
    /// Component hỗ trợ kiểm tra nhanh các chức năng của SaveManager trực tiếp trong Unity Inspector.
    /// Kéo script này vào bất kỳ GameObject nào trong Scene để test.
    /// </summary>
    public class SaveSystemTester : MonoBehaviour
    {
        [Header("Nhập ID Map muốn thử mở khóa")]
        [SerializeField] private string _mapToUnlock = "map_02_vuon_cat_chu";

        [Header("Số xu muốn thử cộng")]
        [SerializeField] private int _coinsToAdd = 50;

        [ContextMenu("▶ Thử: Mở khóa Map")]
        public void TestUnlockMap()
        {
            SaveManager.Instance.UnlockMap(_mapToUnlock);
        }

        [ContextMenu("▶ Thử: Kiểm tra Map đã mở chưa?")]
        public void TestCheckMap()
        {
            bool unlocked = SaveManager.Instance.IsMapUnlocked(_mapToUnlock);
            Debug.Log($"Trạng thái map '{_mapToUnlock}': {(unlocked ? "<color=green>ĐÃ MỞ KHÓA</color>" : "<color=red>CHƯA MỞ</color>")}");
        }

        [ContextMenu("▶ Thử: Cộng 50 Xu Xoài")]
        public void TestAddCoins()
        {
            SaveManager.Instance.AddMangoCoins(_coinsToAdd);
        }

        [ContextMenu("▶ Thử: Mở thư mục chứa file Save")]
        public void TestOpenFolder()
        {
            SaveManager.Instance.OpenSaveFolder();
        }
        [ContextMenu("Thử: Đưa số dư về 100 xu")]
        public void TestResetCoinsTo100()
        {
            var saveManager = SaveManager.Instance;

            if (saveManager == null || saveManager.CurrentData == null)
            {
                Debug.LogWarning("[SaveSystemTester] Chưa có dữ liệu người chơi.");
                return;
            }

            int currentCoins = saveManager.CurrentData.mangoCoins;

            if (currentCoins != 400)
            {
                Debug.LogWarning(
                    $"Số dư hiện tại là {currentCoins}, không phải 400. " +
                    "Không thực hiện thay đổi."
                );
                return;
            }

            bool success = saveManager.SpendMangoCoins(300);

            if (success)
            {
                Debug.Log("[SaveSystemTester] Đã yêu cầu trừ 300 xu.");
            }
            else
            {
                Debug.LogWarning("[SaveSystemTester] Không thể trừ xu.");
            }
        }
    }
}
