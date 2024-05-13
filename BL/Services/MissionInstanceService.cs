using BL.Extensions;
using BL.Logging;
using Newtonsoft.Json;
using System.Reflection;
using Translators.Models;
using Translators.Translators;

namespace BL.Services
{
    public class MissionInstanceService
    {
        public List<MissionInstance> GetMissionInstances()
        {
            using var db = new DataLayer.ShabzakDB();
            var ret = db.MissionInstances
                .ToList()
                .Select(m => m.ToBL())
                .ToList();
            return ret;
        }
        public MissionInstance AddMissionInstance(MissionInstance missionInstance) => AddMissionInstance(missionInstance.ToDB());

        public MissionInstance AddMissionInstance(DataLayer.Models.MissionInstance missionInstance)
        {
            Logger.Log($"Adding MissionInstance:\n {JsonConvert.SerializeObject(missionInstance, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                db.MissionInstances.Add(missionInstance);
                db.SaveChanges();
                return missionInstance.ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while adding MissionInstance:\n{ex}");
                throw;
            }
        }

        public MissionInstance UpdateMissionInstance(MissionInstance missionInstance) => UpdateMissionInstance(missionInstance.ToDB());

        public MissionInstance UpdateMissionInstance(DataLayer.Models.MissionInstance missionInstance)
        {
            Logger.Log($"Updating MissionInstance:\n {JsonConvert.SerializeObject(missionInstance, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var dbModel = db.MissionInstances
                    .FirstOrDefault(m => m.Id == missionInstance.Id) ?? throw new ArgumentException("MissionInstance not found.");
                dbModel.MissionId = missionInstance.MissionId;
                dbModel.FromTime = missionInstance.FromTime;
                dbModel.ToTime = missionInstance.ToTime;
                db.SaveChanges();
                return dbModel.ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while updating MissionInstance:\n{ex}");
                throw;
            }
        }

        public int DeleteMissionInstance(int missionInstanceId)
        {
            Logger.Log($"Deleting MissionInstance {missionInstanceId}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var missionInstance = db.MissionInstances
                    .FirstOrDefault(m => m.Id == missionInstanceId) ?? throw new ArgumentException("MissionInstance not found.");
                Logger.Log($"Found MissionInstance:\n {JsonConvert.SerializeObject(missionInstance, Formatting.Indented)}");
                db.MissionInstances.Remove(missionInstance);
                db.SaveChanges();
                return missionInstanceId;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while deleting MissionInstance:\n{ex}");
                throw;
            }
        }
    }
}
