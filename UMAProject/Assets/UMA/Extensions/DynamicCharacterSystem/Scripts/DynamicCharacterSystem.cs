using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using System;
using UMA;
using UMAAssetBundleManager;

namespace UMACharacterSystem
{
	public class DynamicCharacterSystem : DynamicCharacterSystemBase
	{
		public Dictionary<string, UMATextRecipe> RecipeIndex = new Dictionary<string, UMATextRecipe>();
		public Dictionary<string, Dictionary<string, List<UMATextRecipe>>> Recipes = new Dictionary<string, Dictionary<string, List<UMATextRecipe>>>();
		public Dictionary<string, string> CharacterRecipes = new Dictionary<string, string>();

		public bool initializeOnAwake = true;

		[HideInInspector]
		[System.NonSerialized]
		public bool initialized = false;
		private bool isInitializing = false;

		public bool dynamicallyAddFromResources;
		[Tooltip("Limit the Resources search to the following folders (no starting slash and seperate multiple entries with a comma)")]
		public string resourcesCharactersFolder = "CharacterRecipes";
		[Tooltip("Limit the Resources search to the following folders (no starting slash and seperate multiple entries with a comma)")]
		public string resourcesRecipesFolder = "Recipes";
		public bool dynamicallyAddFromAssetBundles;
		[Tooltip("Limit the AssetBundles search to the following bundles (no starting slash and seperate multiple entries with a comma)")]
		public string assetBundlesForCharactersToSearch;
		[Tooltip("Limit the AssetBundles search to the following bundles (no starting slash and seperate multiple entries with a comma)")]
		public string assetBundlesForRecipesToSearch;
		[Tooltip("If true will automatically scan and add all UMATextRecipes from any downloaded bundles.")]
		public bool addAllRecipesFromDownloadedBundles = true;
		[HideInInspector]
		public UMAContext context;
		//This is a ditionary of asset bundles that were loaded into the library. This can be queried to store a list of active assetBundles that might be useful to preload etc
		public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();
		[System.NonSerialized]
		[HideInInspector]
		public bool downloadAssetsEnabled = true;

		public override void Awake()
        {
            if (initializeOnAwake)
            {
                if (!initialized)
                {
                    Init();
                }
            }
        }

		public override void Start()
		{
			if (!initialized)
			{
				Init();
			}

		}

		public override void Init()
		{
			if (initialized || isInitializing)
			{
				return;
			}
			if (context == null)
			{
				context = UMAContext.FindInstance();
			}
			isInitializing = true;

			Recipes.Clear();
			var possibleRaces = (context.raceLibrary as DynamicRaceLibrary).GetAllRaces();
			for (int i = 0; i < possibleRaces.Length; i++)
			{
                //we need to check that this is not null- the user may not have downloaded it yet
                if (possibleRaces[i] == null)
                    continue;
                if (DynamicAssetLoader.Instance != null && possibleRaces[i].raceName == DynamicAssetLoader.Instance.placeholderRace.raceName)
                    continue;
                Recipes.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
            }

            GatherCharacterRecipes();
			GatherRecipeFiles();
			initialized = true;
			isInitializing = false;
		}
		/// <summary>
		/// Ensures DCS has a race key for the given race in its dictionaries. Use when you want to add recipes to DCS before the actual racedata has been downloaded
		/// </summary>
		public void EnsureRaceKey(string race)
		{
			if (!Recipes.ContainsKey(race))
			{
				Recipes.Add(race, new Dictionary<string, List<UMATextRecipe>>());
			}
		}
		/// <summary>
		/// Refreshes DCS Dictionaries based on the current races in the RaceLibrary. Ensures any newly added races get backwards compatible recipes assigned to them
		/// </summary>
		public void RefreshRaceKeys()
		{
			if (!initialized)
			{
				Init();
				return;
			}
			if (addAllRecipesFromDownloadedBundles)
			{
				Refresh(false, "");
				return;
			}
			var possibleRaces = (context.raceLibrary as DynamicRaceLibrary).GetAllRacesBase();
			for (int i = 0; i < possibleRaces.Length; i++)
			{
				if (!Recipes.ContainsKey(possibleRaces[i].raceName) && possibleRaces[i].raceName != DynamicAssetLoader.Instance.placeholderRace.raceName)
				{
					Recipes.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
					//then make sure any currently added recipes are also assigned to this race if they are compatible
					foreach (string race in Recipes.Keys)
					{
						if (race != possibleRaces[i].raceName)
						{
							foreach (KeyValuePair<string, List<UMATextRecipe>> kp in Recipes[race])
							{
								AddRecipes(kp.Value.ToArray());
							}
						}
					}
				}
			}
		}
		/// <summary>
		/// Forces DCS to update its recipes to include all recipes that are in Resources or in downloaded assetBundles (optionally filtering by assetBundle name)
		/// </summary>
		/// <param name="forceUpdateRaceLibrary">If true will force RaceLibrary to add any races that were in any downloaded assetBundles and then call this Refresh again.</param>
		/// <param name="bundleToGather">Limit the recipes found tto a defined asset bundle</param>
		public override void Refresh(bool forceUpdateRaceLibrary = true, string bundleToGather = "")
		{
			if (!initialized)
			{
				Init();
				return;
			}
			RaceData[] possibleRaces = new RaceData[0];
			if (forceUpdateRaceLibrary)
			{
				possibleRaces = context.raceLibrary.GetAllRaces();//if any new races are added by this then RaceLibrary will Re-Trigger this if there was anything new so dont do anything else
			}
			else
			{
				possibleRaces = (context.raceLibrary as DynamicRaceLibrary).GetAllRacesBase();
				for (int i = 0; i < possibleRaces.Length; i++)
				{
					//we need to check that this is not null- the user may not have downloaded it yet
					if (possibleRaces[i] != null)
					{
						if (!Recipes.ContainsKey(possibleRaces[i].raceName) && possibleRaces[i].raceName != DynamicAssetLoader.Instance.placeholderRace.raceName)
						{
							Recipes.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
						}
					}
				}
				GatherCharacterRecipes("", bundleToGather);
				GatherRecipeFiles("", bundleToGather); 
			}
		}

		private void GatherCharacterRecipes(string filename = "", string bundleToGather = "")
		{
			var assetBundleToGather = bundleToGather != "" ? bundleToGather : assetBundlesForCharactersToSearch;
			//DCS may do this before DAL has downloaded the AssetBundleIndex so in that case we want to turn 'downloadAssetsEnabled' off 
			if (DynamicAssetLoader.Instance != null)
			{
				bool downloadAssetsEnabledNow = DynamicAssetLoader.Instance.isInitialized ? downloadAssetsEnabled : false;
                DynamicAssetLoader.Instance.AddAssets<TextAsset>(ref assetBundlesUsedDict, dynamicallyAddFromResources, dynamicallyAddFromAssetBundles, downloadAssetsEnabledNow, assetBundleToGather, resourcesCharactersFolder, null, filename, AddCharacterRecipes);
			}
		}

		private void AddCharacterRecipes(TextAsset[] characterRecipes)
		{
			foreach (TextAsset characterRecipe in characterRecipes)
			{
				if (!CharacterRecipes.ContainsKey(characterRecipe.name))
					CharacterRecipes.Add(characterRecipe.name, characterRecipe.text);
				else
					CharacterRecipes[characterRecipe.name] = characterRecipe.text;
			}
			//This doesn't actually seem to do anything apart from slow things down- maybe we can hook into the UMAGarbage collection rate and do this at the same time? Or just after?
			//StartCoroutine(CleanFilesFromResourcesAndBundles());
		}

		private void GatherRecipeFiles(string filename = "", string bundleToGather = "")
		{
			var assetBundleToGather = bundleToGather != "" ? bundleToGather : assetBundlesForRecipesToSearch;
			//DCS may do this before DAL has downloaded the AssetBundleIndex so in that case we want to turn 'downloadAssetsEnabled' off 
			if (DynamicAssetLoader.Instance != null)
			{
				bool downloadAssetsEnabledNow = DynamicAssetLoader.Instance.isInitialized ? downloadAssetsEnabled : false;
				//if we are only adding stuff from a downloaded assetbundle, dont search resources
				bool dynamicallyAddFromResourcesNow = bundleToGather == "" ? dynamicallyAddFromResources : false;
				bool found = false;
				found = DynamicAssetLoader.Instance.AddAssets<UMATextRecipe>(ref assetBundlesUsedDict, dynamicallyAddFromResourcesNow, dynamicallyAddFromAssetBundles, downloadAssetsEnabledNow, assetBundleToGather, resourcesRecipesFolder, null, filename, AddRecipesFromAB);
				if (!found || filename != "")
					DynamicAssetLoader.Instance.AddAssets<UMAWardrobeCollection>(ref assetBundlesUsedDict, dynamicallyAddFromResourcesNow, dynamicallyAddFromAssetBundles, downloadAssetsEnabledNow, assetBundleToGather, resourcesRecipesFolder, null, filename, AddRecipesFromAB);
			}
		}

		/*IEnumerator CleanFilesFromResourcesAndBundles()
        {
            yield return null;
            Resources.UnloadUnusedAssets();
            yield break;
        }*/

		public void AddRecipesFromAB(UMATextRecipe[] uparts)
		{
			AddRecipes(uparts, "");
		}

		public void AddRecipe(UMATextRecipe upart)
		{
			if (upart != null)
				AddRecipes(new UMATextRecipe[] { upart });
		}

		public void AddRecipes(UMATextRecipe[] uparts, string filename = "")
		{
			foreach (UMATextRecipe u in uparts)
			{
				if (filename == "" || (filename != "" && filename.Trim() == u.name))
				{
					//we might be refreshing so check its not already there
					if (!RecipeIndex.ContainsKey(u.name))
						RecipeIndex.Add(u.name, u);
					else
					{
						RecipeIndex[u.name] = u;
					}
					var thisWardrobeSlot = u.wardrobeSlot;
					if (u.GetType() == typeof(UMAWardrobeCollection))
					{
						Debug.Log(u.name + " was typeof(UMAWardrobeCollection)");
						thisWardrobeSlot = "FullOutfit";
					}
					for (int i = 0; i < u.compatibleRaces.Count; i++)
					{
						//When races that are compatible with multiple races are downloaded we may not have all the races actually downloaded
						//but that should not stop DCS making an index of recipes that are compatible with that race for when it becomes available
						if (!Recipes.ContainsKey(u.compatibleRaces[i]))
						{
							Recipes.Add(u.compatibleRaces[i], new Dictionary<string, List<UMATextRecipe>>());
						}
						if (Recipes.ContainsKey(u.compatibleRaces[i]))
						{
							Dictionary<string, List<UMATextRecipe>> RaceRecipes = Recipes[u.compatibleRaces[i]];

							if (!RaceRecipes.ContainsKey(thisWardrobeSlot))
							{
								RaceRecipes.Add(thisWardrobeSlot, new List<UMATextRecipe>());
							}
							//we might be refreshing so replace anything that is already there with the downloaded versions- else add
							bool added = false;
							for (int ir = 0; ir < RaceRecipes[thisWardrobeSlot].Count; ir++)
							{
								if (RaceRecipes[thisWardrobeSlot][ir].name == u.name)
								{
									RaceRecipes[thisWardrobeSlot][ir] = u;
									added = true;
								}
							}
							if (!added)
							{
								RaceRecipes[thisWardrobeSlot].Add(u);
							}
						}
						//backwards compatible race slots
						foreach (string racekey in Recipes.Keys)
						{
							//here we also need to check that the race itself has a wardrobe slot that matches the one in the compatible race
							//11012017 Dont trigger backwards compatible races to download
							RaceData raceKeyRace = (context.raceLibrary as DynamicRaceLibrary).GetRace(racekey, false);
							if (raceKeyRace == null)
								continue;
							if (raceKeyRace.backwardsCompatibleWith.Contains(u.compatibleRaces[i]) && raceKeyRace.wardrobeSlots.Contains(thisWardrobeSlot))
							{
								Dictionary<string, List<UMATextRecipe>> RaceRecipes = Recipes[racekey];
								if (!RaceRecipes.ContainsKey(thisWardrobeSlot))
								{
									RaceRecipes.Add(thisWardrobeSlot, new List<UMATextRecipe>());
								}
								//we might be refreshing so replace anything that is already there with the downloaded versions- else add
								bool added = false;
								for (int ir = 0; ir < RaceRecipes[thisWardrobeSlot].Count; ir++)
								{
									if (RaceRecipes[thisWardrobeSlot][ir].name == u.name)
									{
										RaceRecipes[thisWardrobeSlot][ir] = u;
										added = true;
									}
								}
								if (!added)
								{
									RaceRecipes[thisWardrobeSlot].Add(u);
								}
							}
						}
					}
				}
			}
			//This doesn't actually seem to do anything apart from slow things down
			//StartCoroutine(CleanFilesFromResourcesAndBundles());
		}
		//so that Recipe editor can get some info from Recipes
		public override List<string> GetRecipeNamesForRaceSlot(string race, string slot)
		{
			Refresh();
			List<string> recipeNamesForRaceSlot = new List<string>();
			if (Recipes.ContainsKey(race))
			{
				if (Recipes[race].ContainsKey(slot))
				{
					foreach (UMATextRecipe utr in Recipes[race][slot])
					{
						recipeNamesForRaceSlot.Add(utr.name);
					}
				}
			}
			return recipeNamesForRaceSlot;
		}
		//this has to be here so recipe editor (which is in standardAssets) can use it
		public override UMARecipeBase GetBaseRecipe(string filename, bool dynamicallyAdd = true)
		{
			return GetRecipe(filename, dynamicallyAdd);
		}

		public UMATextRecipe GetRecipe(string filename, bool dynamicallyAdd = true)
		{
			UMATextRecipe foundRecipe = null;
			if (RecipeIndex.ContainsKey(filename))
			{
				foundRecipe = RecipeIndex[filename];
			}
			else
			{
				if (dynamicallyAdd)
				{
					GatherRecipeFiles(filename);
					if (RecipeIndex.ContainsKey(filename))
					{
						foundRecipe = RecipeIndex[filename];
					}
				}
			}
			return foundRecipe;
		}
		/// <summary>
		/// Gets the originating asset bundle for a given recipe.
		/// </summary>
		/// <returns>The originating asset bundle.</returns>
		/// <param name="recipeName">Recipe name.</param>
		public string GetOriginatingAssetBundle(string recipeName)
		{
			string originatingAssetBundle = "";
			if (assetBundlesUsedDict.Count == 0)
				return originatingAssetBundle;
			else
			{
				foreach (KeyValuePair<string, List<string>> kp in assetBundlesUsedDict)
				{
					if (kp.Value.Contains(recipeName))
					{
						originatingAssetBundle = kp.Key;
						break;
					}
				}
			}
			if (originatingAssetBundle == "")
			{
				Debug.Log(recipeName + " was not found in any loaded AssetBundle");
			}
			else
			{
				Debug.Log("originatingAssetBundle for " + recipeName + " was " + originatingAssetBundle);
			}
			return originatingAssetBundle;
		}
	}
}
