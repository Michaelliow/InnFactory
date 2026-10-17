using System;
using UnityEngine;

namespace Inn
{
    /// <summary>全部数值的总表，由 Editor/InnDataFactory.cs 生成到 Resources/GameDatabase.asset</summary>
    public class GameDatabase : ScriptableObject
    {
        [Header("内容")]
        public ItemDef[] items;
        public CropDef[] crops;
        public RecipeDef[] recipes;
        public BuildableDef[] buildables;
        public CustomerDef[] customers;
        public ItemDef darkDish;
        public Material baseMaterial;

        [Header("地图")]
        public int mapHeight = 10;
        public int gardenWidth = 12, kitchenWidth = 8, diningWidth = 10;
        public Vector2Int doorRows = new Vector2Int(3, 6);
        public LayoutEntry[] startLayout;

        [Header("经济")]
        public int startGold = 500;
        public ItemCount[] startBag;
        public float dayLengthSeconds = 480;   // 06:00 → 22:00
        public int[] repThresholds;            // 名望等级 1..10 所需累计名望
        public int upkeepBase = 10;
        public float upkeepPerBuildValue = 0.01f;
        public float waterSeconds = 30;

        [Header("顾客")]
        public float arrivalBase = 20;
        public float arrivalPerLevel = 0.15f;
        public float doorWaitSeconds = 15;
        public float eatSeconds = 5;
        public int baseSatisfaction = 60;
        public int decorCap = 20;
        public int likeBonus = 10;
        public int darkPenalty = 30;
        public float waitPenaltyPerSec = 2;
        public int angryLeaveAt = 20;
        public float[] qualityPriceMul = { 1, 1, 1.5f, 2.5f };
        public int[] qualitySatBonus = { 0, 0, 10, 20 };

        [Header("员工")]
        public StarRow[] starRows;
        public int recruitCount = 3;
        public int recruitRefreshCost = 30;
        public int staffBaseCap = 2;
        public string[] surnames;
        public string[] givenNames;

        [Header("玩家")]
        public float playerSpeed = 4.5f;
        public int playerCooking = 30;
        public float interactRange = 1.7f;

        public static GameDatabase Load() => Resources.Load<GameDatabase>("GameDatabase");

        public BuildableDef Buildable(string id) => Array.Find(buildables, b => b.id == id);
        public ItemDef Item(string id) => Array.Find(items, b => b.id == id);
        public CropDef Crop(string id) => Array.Find(crops, b => b.id == id);
        public int MapWidth => gardenWidth + kitchenWidth + diningWidth;
    }
}
