using System;
using UnityEngine;

namespace Toufuku.GameInput
{
    public class SideSwingDetector : MonoBehaviour
    {
        [Header("‰¡U‚èŒŸ’m")]
        [SerializeField] private float triggerVelocity = 350f;
        [SerializeField] private float releaseVelocity = 100f;

        [Header("cU‚èœŠO")]
        [Tooltip("Pitch‚ÌŠp‘¬“x‚ª‚±‚Ì’l‚ğ’´‚¦‚½‚ç‰¡U‚è‚Æ”»’è‚µ‚È‚¢")]
        [SerializeField] private float maxPitchVelocity = 250f;

        [Tooltip("Yaw‚ÌŠp‘¬“x‚ª‚±‚Ì’l‚ğ’´‚¦‚½‚ç‰¡U‚è‚Æ”»’è‚µ‚È‚¢")]
        [SerializeField] private float maxYawVelocity = 300f;

        [Header("ƒfƒoƒbƒO")]
        [SerializeField] private bool showDebugLog = true;

        public event Action SideSwingDetected;

        private float previousYaw;
        private float previousPitch;
        private float previousRoll;
        private double previousTime;

        private bool hasPreviousSample;
        private bool swingActive;

        public void AddSample(float yaw, float pitch, float roll, double time)
        {
            if (!hasPreviousSample)
            {
                SaveSample(yaw, pitch, roll, time);
                hasPreviousSample = true;
                return;
            }

            double deltaTime = time - previousTime;

            if (deltaTime <= 0.0001)
            {
                SaveSample(yaw, pitch, roll, time);
                return;
            }

            float yawVelocity =
    Mathf.DeltaAngle(previousYaw, yaw) / (float)deltaTime;

            float pitchVelocity =
                Mathf.DeltaAngle(previousPitch, pitch) / (float)deltaTime;

            float rollVelocity =
                Mathf.DeltaAngle(previousRoll, roll) / (float)deltaTime;

            SaveSample(yaw, pitch, roll, time);

            // ‚¢‚Á‚½‚ñU‚èn‚ß‚½‚çA‘¬“x‚ª—‚¿‚é‚Ü‚ÅÄ”­“®‚µ‚È‚¢
            if (swingActive)
            {
                if (Mathf.Abs(yawVelocity) <= releaseVelocity)
                    swingActive = false;

                return;
            }

            // Roll‚Ì‘¬“x‚ª‘«‚è‚È‚¯‚ê‚Î‰¡U‚èŒó•â‚Å‚Í‚È‚¢
            if (Mathf.Abs(yawVelocity) < triggerVelocity)
                return;

            // Pitch‚Ü‚½‚ÍYaw‚Ì•Ï‰»‚ª‘å‚«‚¢ê‡‚ÍcU‚è‚È‚Ç‚Æ‚İ‚È‚µ‚ÄœŠO
            if (Mathf.Abs(pitchVelocity) > maxPitchVelocity ||
    Mathf.Abs(rollVelocity) > maxYawVelocity)
            {
                return;
            }

            swingActive = true;

            if (showDebugLog)
            {
                Debug.Log(
                    $"[‰¡U‚èŒŸ’m] Yaw:{yawVelocity:F1}, " +
                    $"Pitch:{pitchVelocity:F1}, Roll:{rollVelocity:F1}",
                    this
                );
            }

            SideSwingDetected?.Invoke();
        }

        private void SaveSample(
            float yaw,
            float pitch,
            float roll,
            double time)
        {
            previousYaw = yaw;
            previousPitch = pitch;
            previousRoll = roll;
            previousTime = time;
        }

        public void ResetDetector()
        {
            hasPreviousSample = false;
            swingActive = false;
        }
    }
}