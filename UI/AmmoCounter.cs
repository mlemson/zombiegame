using TMPro;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    [RequireComponent(typeof(FillBarColorChange))]
    public class AmmoCounter : MonoBehaviour
    {
        [Tooltip("CanvasGroup to fade the ammo UI")]
        public CanvasGroup CanvasGroup;

        [Tooltip("Image for the weapon icon")] public Image WeaponImage;

        [Tooltip("Image component for the background")]
        public Image AmmoBackgroundImage;

        [Tooltip("Image component to display fill ratio")]
        public Image AmmoFillImage;

        [Tooltip("Text for Weapon index")] 
        public TextMeshProUGUI WeaponIndexText;

        [Tooltip("Text for Bullet Counter")] 
        public TextMeshProUGUI BulletCounter;

        [Tooltip("Reload Text for Weapons with physical bullets")]
        public RectTransform Reload;

        [Header("Selection")] [Range(0, 1)] [Tooltip("Opacity when weapon not selected")]
        public float UnselectedOpacity = 0.5f;

        [Tooltip("Scale when weapon not selected")]
        public Vector3 UnselectedScale = Vector3.one * 0.8f;

        [Tooltip("Root for the control keys")] public GameObject ControlKeysRoot;

        [Header("Feedback")] [Tooltip("Component to animate the color when empty or full")]
        public FillBarColorChange FillBarColorChange;

        [Tooltip("Sharpness for the fill ratio movements")]
        public float AmmoFillMovementSharpness = 20f;

        public int WeaponCounterIndex { get; set; }

        PlayerWeaponsManager m_PlayerWeaponsManager;
        WeaponController m_Weapon;

        void Awake()
        {
            EventManager.AddListener<AmmoPickupEvent>(OnAmmoPickup);
        }

        void OnAmmoPickup(AmmoPickupEvent evt)
        {
            if (evt.Weapon == m_Weapon)
            {
                UpdateAmmoText();
            }
        }

        public void Initialize(WeaponController weapon, int weaponIndex)
        {
            m_Weapon = weapon;
            WeaponCounterIndex = weaponIndex;
            WeaponImage.sprite = weapon.WeaponIcon;
            BulletCounter.transform.parent.gameObject.SetActive(!weapon.IsMeleeWeapon);
            ConfigureNumericCounter();
            UpdateAmmoText();

            Reload.gameObject.SetActive(false);
            m_PlayerWeaponsManager = FindAnyObjectByType<PlayerWeaponsManager>();
            DebugUtility.HandleErrorIfNullFindObject<PlayerWeaponsManager, AmmoCounter>(m_PlayerWeaponsManager, this);

            WeaponIndexText.text = (WeaponCounterIndex + 1).ToString();

            AmmoFillImage.enabled = false;
            AmmoBackgroundImage.enabled = false;
            FillBarColorChange.enabled = false;
        }

        void Update()
        {
            UpdateAmmoText();

            bool isActiveWeapon = m_Weapon == m_PlayerWeaponsManager.GetActiveWeapon();

            CanvasGroup.alpha = Mathf.Lerp(CanvasGroup.alpha, isActiveWeapon ? 1f : UnselectedOpacity,
                Time.deltaTime * 10);
            transform.localScale = Vector3.Lerp(transform.localScale, isActiveWeapon ? Vector3.one : UnselectedScale,
                Time.deltaTime * 10);
            ControlKeysRoot.SetActive(!isActiveWeapon);

            Reload.gameObject.SetActive(!m_Weapon.IsMeleeWeapon && m_Weapon.GetCarriedPhysicalBullets() > 0 && m_Weapon.GetCurrentAmmo() == 0 && m_Weapon.IsWeaponActive);
        }

        void ConfigureNumericCounter()
        {
            RectTransform counterRoot = BulletCounter.transform.parent as RectTransform;
            if (counterRoot != null)
            {
                counterRoot.sizeDelta = new Vector2(116f, 32f);
                counterRoot.anchoredPosition = new Vector2(45f, 0f);
            }

            BulletCounter.rectTransform.sizeDelta = new Vector2(116f, 32f);
            BulletCounter.fontSize = 18f;
            BulletCounter.textWrappingMode = TextWrappingModes.NoWrap;
            BulletCounter.alignment = TextAlignmentOptions.Center;
        }

        void UpdateAmmoText()
        {
            if (m_Weapon == null)
                return;

            BulletCounter.text = m_Weapon.HasPhysicalBullets
                ? $"{m_Weapon.GetCurrentAmmo()} / {m_Weapon.GetCarriedPhysicalBullets()}"
                : m_Weapon.GetCurrentAmmo().ToString();
        }

        void OnDestroy()
        {
            EventManager.RemoveListener<AmmoPickupEvent>(OnAmmoPickup);
        }
    }
}
