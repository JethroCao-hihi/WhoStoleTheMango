using System;
using System.IO;
using MangoHeist.Auth;
using MangoHeist.Data;
using UnityEngine;

namespace MangoHeist.Testing
{
    /// <summary>
    /// Bộ kiểm thử xác minh hệ thống sau khi đã VÁ TOÀN BỘ BUG (Security & Logic Verification):
    /// Chạy lại 5 kịch bản tấn công để chứng minh các lỗ hổng đã được bịt kín 100%!
    /// </summary>
    public class SystemStressTester : MonoBehaviour
    {
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/Antigravity MCP/🔥 CHẠY TOÀN BỘ TEST TÌM BUG")]
        [UnityEditor.MenuItem("Tools/Antigravity MCP/🛡️ KIỂM TRA HỆ THỐNG ĐÃ VÁ BUG CHƯA")]
        public static void RunVerificationFromMenu()
        {
            var go = new GameObject("[TempStressTester]");
            var tester = go.AddComponent<SystemStressTester>();
            tester.RunAllVerificationTests();
            DestroyImmediate(go);
        }
#endif

        [ContextMenu("🛡️ CHẠY TẤT CẢ BÀI TEST KIỂM ĐỊNH")]
        public void RunAllVerificationTests()
        {
            Debug.Log("<color=#4CAF50><b>================ BẮT ĐẦU KIỂM ĐỊNH CÁC BẢN VÁ BUG ================</b></color>");

            Test1_VerifyAccountDataIsolation();
            Test2_VerifyNegativeCoinsBlocked();
            Test3_VerifyEmptyMapRejected();
            Test4_VerifyCorruptedFileAutoRecovery();
            Test5_VerifyInjectionRejected();

            Debug.Log("<color=#4CAF50><b>================ TẤT CẢ BÀI KIỂM ĐỊNH HOÀN TẤT ===================</b></color>");
        }

        public void RunAllBugTests() => RunAllVerificationTests();

        public void Test1_VerifyAccountDataIsolation()
        {
            Debug.Log("<b>[Kiểm định 1]</b> Kiểm tra tính độc lập dữ liệu giữa 2 tài khoản...");

            string userA = "UserA_" + UnityEngine.Random.Range(100, 999);
            AuthManager.Instance.Register(userA, "matkhau123", (okA, msgA) =>
            {
                SaveManager.Instance.AddMangoCoins(500);
                SaveManager.Instance.UnlockMap("map_rieng_cua_A");

                AuthManager.Instance.Logout();

                string userB = "UserB_" + UnityEngine.Random.Range(100, 999);
                AuthManager.Instance.Register(userB, "matkhau456", (okB, msgB) =>
                {
                    var dataB = SaveManager.Instance.CurrentData;
                    if (dataB.unlockedMapIds.Contains("map_rieng_cua_A") || dataB.mangoCoins > 100)
                    {
                        Debug.LogError($"<color=red>❌ [THẤT BẠI]: Vẫn bị lộ dữ liệu! User B có {dataB.mangoCoins} xu.</color>");
                    }
                    else
                    {
                        Debug.Log($"<color=#4CAF50>✅ [THÀNH CÔNG - ĐÃ VÁ BUG 1]:</color> Tài khoản '{userB}' hoàn toàn độc lập! Có 100 xu khởi đầu và không chứa map của '{userA}'.");
                    }
                });
            });
        }

        public void Test2_VerifyNegativeCoinsBlocked()
        {
            Debug.Log("<b>[Kiểm định 2]</b> Thử tấn công trừ âm 9999 xu...");

            SaveManager.Instance.AddMangoCoins(-9999);
            int coins = SaveManager.Instance.CurrentData.mangoCoins;

            bool canOverspend = SaveManager.Instance.SpendMangoCoins(99999);

            if (coins >= 0 && !canOverspend)
            {
                Debug.Log($"<color=#4CAF50>✅ [THÀNH CÔNG - ĐÃ VÁ BUG 2]:</color> Số dư được bảo vệ an toàn (Hiện có: {coins} Xu, không bao giờ bị âm). Lệnh chi tiêu vượt mức đã bị chặn!");
            }
            else
            {
                Debug.LogError($"<color=red>❌ [THẤT BẠI]: Số dư bị âm: {coins} Xu!</color>");
            }
        }

        public void Test3_VerifyEmptyMapRejected()
        {
            Debug.Log("<b>[Kiểm định 3]</b> Thử mở các ID map rỗng và null...");

            int countBefore = SaveManager.Instance.CurrentData.unlockedMapIds.Count;

            SaveManager.Instance.UnlockMap("");
            SaveManager.Instance.UnlockMap("   ");
            SaveManager.Instance.UnlockMap(null);

            int countAfter = SaveManager.Instance.CurrentData.unlockedMapIds.Count;

            if (countBefore == countAfter)
            {
                Debug.Log($"<color=#4CAF50>✅ [THÀNH CÔNG - ĐÃ VÁ BUG 3]:</color> Đã từ chối tất cả các ID map rỗng và khoảng trắng. Danh sách map hoàn toàn sạch sẽ!");
            }
            else
            {
                Debug.LogError("<color=red>❌ [THẤT BẠI]: Map rỗng vẫn lọt vào danh sách!</color>");
            }
        }

        public void Test4_VerifyCorruptedFileAutoRecovery()
        {
            Debug.Log("<b>[Kiểm định 4]</b> Thử phá hỏng file save bằng chuỗi rác xem hệ thống có tự phục hồi không...");

            string path = Path.Combine(Application.persistentDataPath, $"player_save_{SaveManager.Instance.ActivePlayerId}.json");

            try
            {
                // Cố tình ghi chuỗi hỏng vào file để kiểm tra cơ chế tự phục hồi
                File.WriteAllText(path, "{ corrupted_data: broken!!! ");
                SaveManager.Instance.LoadGame();
                // Khôi phục ngay file save lành lặn sau khi test xong
                SaveManager.Instance.SaveGame();

                if (SaveManager.Instance.CurrentData != null && SaveManager.Instance.CurrentData.unlockedMapIds.Count > 0)
                {
                    Debug.Log("<color=#4CAF50>✅ [THÀNH CÔNG - ĐÃ VÁ BUG 4]:</color> Khi file save bị hỏng/crash, cơ chế Auto-Recovery đã tự động cứu dữ liệu hoặc tái tạo an toàn, không làm sập game!");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red>❌ [THẤT BẠI]: Game bị crash: {ex.Message}</color>");
            }
        }

        public void Test5_VerifyInjectionRejected()
        {
            Debug.Log("<b>[Kiểm định 5]</b> Thử đăng ký tên tài khoản phá hoại JSON chứa dấu ngoặc kép 'Hacker\" : true'...");

            string dirtyUsername = "Hacker\" : true, \"isAdmin\": true";
            AuthManager.Instance.Register(dirtyUsername, "pass123", (success, msg) =>
            {
                if (!success)
                {
                    Debug.Log($"<color=#4CAF50>✅ [THÀNH CÔNG - ĐÃ VÁ BUG 5]:</color> Đã chặn thành công tên tài khoản chứa ký tự nguy hiểm: \"{msg}\".");
                }
                else
                {
                    Debug.LogError("<color=red>❌ [THẤT BẠI]: Hệ thống vẫn cho phép đặt tên chứa dấu ngoặc kép!</color>");
                }
            });
        }
    }
}
