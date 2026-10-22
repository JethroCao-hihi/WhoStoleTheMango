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
    }
}
