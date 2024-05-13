using BL.Extensions;
using BL.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace BL.Services
{
    public class MissionPositionService
    {
        public List<MissionPosition> GetMissionPositions()
        {
            using var db = new DataLayer.ShabzakDB();
            var ret = db.MissionPositions
                .ToList()
                .Select(m => m.ToBL())
                .ToList();
            return ret;
        }
        public MissionPosition AddMissionPosition(MissionPosition missionInstance) => AddMissionPosition(missionInstance.ToDB());

        public MissionPosition AddMissionPosition(DataLayer.Models.MissionPositions missionInstance)
        {
            Logger.Log($"Adding MissionPosition:\n {JsonConvert.SerializeObject(missionInstance, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                db.MissionPositions.Add(missionInstance);
                db.SaveChanges();
                return missionInstance.ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while adding MissionPosition:\n{ex}");
                throw;
            }
        }

        public MissionPosition UpdateMissionPosition(MissionPosition missionInstance) => UpdateMissionPosition(missionInstance.ToDB());

        public MissionPosition UpdateMissionPosition(DataLayer.Models.MissionPositions missionInstance)
        {
            Logger.Log($"Updating MissionPosition:\n {JsonConvert.SerializeObject(missionInstance, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var dbModel = db.MissionPositions
                    .FirstOrDefault(m => m.Id == missionInstance.Id) ?? throw new ArgumentException("MissionPosition not found.");
                dbModel.MissionId = missionInstance.Id;
                dbModel.Position = missionInstance.Position;
                dbModel.Count = missionInstance.Count;
                db.SaveChanges();
                return dbModel.ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while updating MissionPosition:\n{ex}");
                throw;
            }
        }

        public int DeleteMissionPosition(int missionInstanceId)
        {
            Logger.Log($"Deleting MissionPosition {missionInstanceId}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var missionInstance = db.MissionPositions
                    .FirstOrDefault(m => m.Id == missionInstanceId) ?? throw new ArgumentException("MissionPosition not found.");
                Logger.Log($"Found MissionPosition:\n {JsonConvert.SerializeObject(missionInstance, Formatting.Indented)}");
                db.MissionPositions.Remove(missionInstance);
                db.SaveChanges();
                return missionInstanceId;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while deleting MissionPosition:\n{ex}");
                throw;
            }
        }
    }
}
