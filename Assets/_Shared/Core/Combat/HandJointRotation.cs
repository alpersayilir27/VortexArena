using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Riglenmiş bir parmak ekleminin <b>tek</b> kaydı: hangi eklem + o eklemin YEREL dönüşü.
    /// Bir kavrama kaydının (<see cref="ItemGripPose"/>) parmak duruşu bunlardan bir dizidir.
    /// <para>
    /// ⚠️ <b>Eklem ADIYLA saklanır, indeksle DEĞİL.</b> ISDK'nın eklem listesi
    /// (<c>FingersMetadata.HAND_JOINT_IDS</c>) derleme dalına göre değişiyor: OpenXR dalı 19 eklem
    /// ve metakarpal setiyle, OVR dalı 17 eklem ve başka bir setle geliyor. İndeks saklansaydı dal
    /// değiştiğinde başparmağın dönüşü sessizce işaret parmağına yazılırdı — adın karşılığı yoksa
    /// o satır atlanır, ötekiler doğru kalır. Ad, <c>HandJointId</c> enum değerinin adıdır
    /// (<c>"HandIndex1"</c>) ve asset'in YAML'ında okunabilir durur.
    /// </para>
    /// <para>
    /// ⚠️ <b>Dönüş, ISDK'nın "izlenen" (tracked) uzayındadır</b> — <c>HandJointMap.RotationOffset</c>
    /// UYGULANMADAN önceki hâli. Sebep: aynı dizi hem stüdyodaki hayalet ele
    /// (<c>HandPuppet.SetJointRotations</c>, ofseti kendi ekler) hem oyundaki sentetik ele
    /// (<c>SyntheticHand.OverrideAllJoints</c>) gidiyor; ofsetli hâli saklamak, iki uçtan birinde
    /// ofsetin ikinci kez uygulanması demekti.
    /// </para>
    /// </summary>
    [Serializable]
    public struct HandJointRotation
    {
        [Tooltip("Eklemin adı — HandJointId enum değerinin adı (ör. HandIndex1).")]
        public string joint;

        [Tooltip("Eklemin YEREL dönüşü (ISDK izleme uzayı, RotationOffset uygulanmadan).")]
        public Quaternion rotation;

        /// <summary>Adı ve dönüşü olan bir kayıt üretir.</summary>
        public static HandJointRotation From(string jointName, in Quaternion localRotation)
        {
            return new HandJointRotation
            {
                joint = jointName,
                rotation = localRotation,
            };
        }
    }
}
