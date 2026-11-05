using UnityEngine;
using UnityEngine.SceneManagement;

namespace MangoHeist.UI
{
    /// <summary>
    /// Script đã được vô hiệu hóa để không làm hỏng Scale của Canvas do Unity Editor thiết lập.
    /// </summary>
    internal static class SceneCanvasScaleRepair
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneHook()
        {
            return; // CHẶN KHÔNG CHO CHẠY
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RepairInitialScene()
        {
            return; // CHẶN KHÔNG CHO CHẠY
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            return;
        }

        private static void RepairCanvases()
        {
            // Đã xóa toàn bộ code tự động sửa Scale (localScale = Vector3.one) ở đây
        }
    }
}