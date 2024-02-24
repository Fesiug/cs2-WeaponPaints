﻿using Dapper;
using System.Collections.Concurrent;

namespace WeaponPaints
{
	internal class WeaponSynchronization
	{
		private readonly WeaponPaintsConfig _config;
		private readonly Database _database;

		internal WeaponSynchronization(Database database, WeaponPaintsConfig config)
		{
			_database = database;
			_config = config;
		}

		internal async Task GetKnifeFromDatabase(PlayerInfo player)
		{
			if (!_config.Additional.KnifeEnabled) return;
			try
			{
				await using var connection = await _database.GetConnectionAsync();
				string query = "SELECT `knife` FROM `wp_player_knife` WHERE `steamid` = @steamid";
				string? playerKnife = await connection.QueryFirstOrDefaultAsync<string>(query, new { steamid = player.SteamId });

				if (!string.IsNullOrEmpty(playerKnife))
				{
					WeaponPaints.g_playersKnife[player.Slot] = playerKnife;
				}
			}
			catch (Exception e)
			{
				Utility.Log(e.Message);
				return;
			}
		}

		internal async Task GetGloveFromDatabase(PlayerInfo player)
		{
			if (!_config.Additional.GloveEnabled) return;

			try
			{
				// Ensure proper disposal of resources using "using" statement
				await using var connection = await _database.GetConnectionAsync();

				// Construct the SQL query with specific columns for better performance
				string query = "SELECT `weapon_defindex` FROM `wp_player_gloves` WHERE `steamid` = @steamid";

				// Execute the query and retrieve glove data
				ushort? gloveData = await connection.QueryFirstOrDefaultAsync<ushort?>(query, new { steamid = player.SteamId });

				// Check if glove data is retrieved successfully
				if (gloveData != null)
				{
					// Update g_playersGlove dictionary with glove data
					WeaponPaints.g_playersGlove[player.Slot] = gloveData.Value;
				}
			}
			catch (Exception e)
			{
				// Log any exceptions occurred during database operation
				Utility.Log("An error occurred while fetching glove data: " + e.Message);
			}
		}

		internal async Task GetWeaponPaintsFromDatabase(PlayerInfo player)
		{
			if (!_config.Additional.SkinEnabled || player == null || player.SteamId == null) return;

			try
			{
				await using var connection = await _database.GetConnectionAsync();
                string query = "SELECT `weapon_defindex`, `weapon_paint_id`, `weapon_wear`, `weapon_seed` FROM `wp_player_skins` WHERE `steamid` = @steamid";

                var playerSkins = await connection.QueryAsync<dynamic>(query, new { steamid = player.SteamId });

				if (playerSkins == null) return;

				var weaponInfos = new ConcurrentDictionary<int, WeaponInfo>();

				foreach (var row in playerSkins)
				{
					int weaponDefIndex = row?.weapon_defindex ?? 0;
					int weaponPaintId = row?.weapon_paint_id ?? 0;
					float weaponWear = row?.weapon_wear ?? 0f;
					int weaponSeed = row?.weapon_seed ?? 0;

					WeaponInfo weaponInfo = new WeaponInfo
					{
						Paint = weaponPaintId,
						Seed = weaponSeed,
						Wear = weaponWear
					};

					weaponInfos[weaponDefIndex] = weaponInfo;
				}

				WeaponPaints.gPlayerWeaponsInfo[player.Slot] = weaponInfos;
			}
			catch (Exception e)
			{
				Utility.Log($"Database error occurred: {e.Message}");
			}
		}

		internal async Task SyncKnifeToDatabase(PlayerInfo player, string knife)
		{
			if (!_config.Additional.KnifeEnabled || player == null || string.IsNullOrEmpty(player.SteamId) || string.IsNullOrEmpty(knife)) return;

			try
			{
				await using var connection = await _database.GetConnectionAsync();
				string query = "INSERT INTO `wp_player_knife` (`steamid`, `knife`) VALUES(@steamid, @newKnife) ON DUPLICATE KEY UPDATE `knife` = @newKnife";
				await connection.ExecuteAsync(query, new { steamid = player.SteamId, newKnife = knife });
			}
			catch (Exception e)
			{
				Utility.Log($"Error syncing knife to database: {e.Message}");
			}
		}


		internal async Task SyncGloveToDatabase(PlayerInfo player, int defindex)
		{
			if (!_config.Additional.GloveEnabled || player == null || string.IsNullOrEmpty(player.SteamId)) return;

			try
			{
				await using var connection = await _database.GetConnectionAsync();
				string query = "INSERT INTO `wp_player_gloves` (`steamid`, `weapon_defindex`) VALUES(@steamid, @weapon_defindex) ON DUPLICATE KEY UPDATE `weapon_defindex` = @weapon_defindex";
				await connection.ExecuteAsync(query, new { steamid = player.SteamId, weapon_defindex = defindex });
			}
			catch (Exception e)
			{
				Utility.Log($"Error syncing glove to database: {e.Message}");
			}
		}

		internal async Task SyncWeaponPaintsToDatabase(PlayerInfo player)
		{
			if (player == null || string.IsNullOrEmpty(player.SteamId) || !WeaponPaints.gPlayerWeaponsInfo.TryGetValue(player.Slot, out var weaponsInfo))
				return;

			try
			{
				await using var connection = await _database.GetConnectionAsync();

                foreach (var weaponInfoPair in weaponsInfo)
                {
                    int weaponDefIndex = weaponInfoPair.Key;
                    WeaponInfo weaponInfo = weaponInfoPair.Value;

                    int paintId = weaponInfo.Paint;
                    float wear = weaponInfo.Wear;
                    int seed = weaponInfo.Seed;

                    string query = "INSERT INTO `wp_player_skins` (`steamid`, `weapon_defindex`, `weapon_paint_id`, `weapon_wear`, `weapon_seed`) " +
                                   "VALUES (@steamid, @weaponDefIndex, @paintId, @wear, @seed) " +
                                   "ON DUPLICATE KEY UPDATE `weapon_paint_id` = @paintId, `weapon_wear` = @wear, `weapon_seed` = @seed";

                    var parameters = new DynamicParameters();
                    parameters.Add("@steamid", player.SteamId);
                    parameters.Add("@weaponDefIndex", weaponDefIndex);
                    parameters.Add("@paintId", paintId);
                    parameters.Add("@wear", wear);
                    parameters.Add("@seed", seed);

                    await connection.ExecuteAsync(query, parameters);
                }

            }
            catch (Exception e)
			{
				Utility.Log($"Error syncing weapon paints to database: {e.Message}");
			}
		}

	}
}