using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Inventory;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Buildings
{
    /// <summary>
    /// 3.10b2: a hand test found no way to see what was actually in the crown stockpile. A HUD
    /// button opens this panel, listing every good the crown storehouse holds with its amount,
    /// refreshed live. Pure presentation: reads the modules, writes nothing.
    /// </summary>
    public sealed class StockpilePanelPresenter : MonoBehaviour
    {
        private const float PopulateIntervalSeconds = 0.5f;

        private BuildingsModule _buildings;
        private InventoryModule _inventory;

        private VisualElement _root;
        private ScrollView _goodsContainer;
        private Button _closeButton;
        private Button _toggleButton;

        private bool _isShowing;
        private float _nextPopulateTime;

        /// <summary>For tests: whether the panel is currently shown.</summary>
        public bool IsShowingForTesting => _isShowing;

        /// <summary>For tests: how many good rows are currently listed.</summary>
        public int GoodRowCountForTesting => _goodsContainer?.childCount ?? 0;

        public void Initialize(BuildingsModule buildings, InventoryModule inventory, VisualElement uiRoot)
        {
            _buildings = buildings;
            _inventory = inventory;

            var asset = Resources.Load<VisualTreeAsset>("StockpilePanel");
            if (asset == null)
            {
                Debug.LogError("[TheEmptying] Could not load StockpilePanel from Resources. The stockpile panel will not render.");
                return;
            }

            asset.CloneTree(uiRoot);

            _root = uiRoot.Q<VisualElement>("stockpile-panel-root");
            _goodsContainer = uiRoot.Q<ScrollView>("stockpile-panel-goods");
            _closeButton = uiRoot.Q<Button>("stockpile-panel-close");
            _closeButton.clicked += Hide;
            _root.style.display = DisplayStyle.None;

            _toggleButton = new Button { text = "Stockpile" };
            _toggleButton.style.position = Position.Absolute;
            _toggleButton.style.top = 8;
            _toggleButton.style.right = 260;
            _toggleButton.clicked += Toggle;
            uiRoot.Add(_toggleButton);
        }

        public void Rebind(BuildingsModule buildings, InventoryModule inventory)
        {
            _buildings = buildings;
            _inventory = inventory;
            Hide();
        }

        public void Toggle()
        {
            if (_isShowing)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Show()
        {
            if (_root == null)
            {
                return;
            }

            _isShowing = true;
            _root.style.display = DisplayStyle.Flex;
            Populate();
        }

        public void Hide()
        {
            _isShowing = false;
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        private void Update()
        {
            if (_isShowing && Time.unscaledTime >= _nextPopulateTime)
            {
                Populate();
                _nextPopulateTime = Time.unscaledTime + PopulateIntervalSeconds;
            }
        }

        private void Populate()
        {
            if (_goodsContainer == null)
            {
                return;
            }

            _goodsContainer.Clear();

            if (_buildings == null || !_buildings.TryFindCrownStorehouse(out EntityId storehouseId))
            {
                _goodsContainer.Add(new Label("No stockpile yet.") { style = { color = Color.gray } });
                return;
            }

            StockpileContentsFormatter.AppendGoodsRows(_goodsContainer, storehouseId, _inventory);
        }
    }
}
