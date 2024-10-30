using Azure;
using DataLayer.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer
{
    public class ShabzakDB: DbContext
    {
        public ShabzakDB()
        {
            
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = @"Data Source=W11GC68DS3\SQLEXPRESS;Initial Catalog=ShabzakDB;Integrated Security=True;Multiple Active Result Sets=True;Trust Server Certificate=True;Packet Size=16000;";
                optionsBuilder.UseSqlServer(connectionString, builder => { 
                    builder.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                });
                base.OnConfiguring(optionsBuilder);
            }
            optionsBuilder.EnableSensitiveDataLogging();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Soldier>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired();
                entity.Property(e => e.PersonalNumber)
                    .IsRequired();
                entity.Property(e => e.Phone)
                    .IsRequired();
                entity.Property(e => e.Position)
                    .IsRequired();
                entity.Property(e => e.Platoon)
                    .IsRequired();
                entity.Property(e => e.Company)
                    .IsRequired();
                entity.Property(e => e.Active)
                .HasDefaultValue(true);

                entity.HasMany(e => e.Missions)
                    .WithOne(e => e.Soldier)
                    .HasForeignKey(e => e.SoldierId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Mission>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired();
                entity.Property(e => e.SoldiersRequired)
                    .IsRequired();
                entity.Property(e => e.CommandersRequired)
                    .IsRequired();
                entity.Property(e => e.Duration)
                    .IsRequired();
                entity.Property(e => e.SimulateDuration)
                    .HasDefaultValue(null);
                entity.Property(e => e.IsSpecial)
                    .IsRequired();

                entity.HasMany(e => e.MissionPositions)
                    .WithOne(e => e.Mission)
                    .HasForeignKey(e => e.MissionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.MissionInstances)
                    .WithOne(e => e.Mission)
                    .HasForeignKey(e => e.MissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MissionInstance>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MissionId)
                    .IsRequired();
                entity.Property(e => e.FromTime);
                entity.Property(e => e.ToTime);

                entity.HasOne(e => e.Mission)
                    .WithMany(e => e.MissionInstances)
                    .HasForeignKey(e => e.MissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SoldierMission>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SoldierId)
                    .IsRequired();
                entity.Property(e => e.MissionInstanceId)
                    .IsRequired();
                entity.Property(e => e.MissionPositionId)
                    .IsRequired();

                entity.HasOne(e => e.Soldier)
                .WithMany(e => e.Missions)
                .HasForeignKey(e => e.SoldierId)
                .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.MissionInstance)
                .WithMany(e => e.Soldiers)
                .HasForeignKey(e => e.MissionInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.MissionPosition)
                .WithMany(e => e.Soldiers)
                .HasForeignKey(e => e.MissionPositionId)
                .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<MissionPositions>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MissionId)
                    .IsRequired();
                entity.Property(e => e.Position)
                    .IsRequired();
                entity.Property(e => e.Count)
                    .IsRequired();

                entity.HasOne(e => e.Mission)
                    .WithMany(e => e.MissionPositions)
                    .HasForeignKey(e => e.MissionId);

                //entity.HasMany(e => e.Soldiers)
                //    .WithOne(e => e.MissionPosition)
                //    .HasForeignKey(e => e.MissionInstanceId);
            });

            modelBuilder.Entity<Vacation>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SoldierId)
                    .IsRequired();
                entity.Property(e => e.From)
                    .IsRequired();
                entity.Property(e => e.To)
                    .IsRequired();

                entity.HasOne(e => e.Soldier)
                    .WithMany(e => e.Vacations)
                    .HasForeignKey(e => e.SoldierId);
            });

            modelBuilder.Entity<SoldierMissionCandidate>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SoldierId)
                    .IsRequired();
                entity.Property(e => e.MissionInstanceId)
                    .IsRequired();
                entity.Property(e => e.MissionPositionId)
                    .IsRequired();
                entity.Property(e => e.CandidateId)
                    .IsRequired();
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired();
                entity.Property(e => e.Password)
                    .IsRequired();
                entity.Property(e => e.Salt)
                    .IsRequired();
                entity.Property(e => e.Role)
                    .IsRequired();
                entity.Property(e => e.Activated)
                    .HasDefaultValue(true)
                    .IsRequired();
                entity.Property(e => e.Enabled)
                    .HasDefaultValue(false)
                    .IsRequired();
            });

            //modelBuilder.Entity<UserToken>(entity =>
            //{
            //    entity.HasKey(e => e.Id);

            //    entity.Property(e => e.Token)
            //        .IsRequired();
            //    entity.Property(e => e.Expiration)
            //        .IsRequired();
            //    entity.Property(e => e.UserId)
            //        .IsRequired();


            //    entity.HasOne(e => e.User)
            //        .WithMany(e => e.UserTokens)
            //        .HasForeignKey(e => e.UserId);
            //});
        }

        public DbSet<Soldier> Soldiers { get; set; }
        public DbSet<Mission> Missions { get; set; }
        public DbSet<MissionInstance> MissionInstances { get; set; }
        public DbSet<MissionPositions> MissionPositions { get; set; }
        public DbSet<SoldierMission> SoldierMission { get; set; }
        public DbSet<SoldierMissionCandidate> SoldierMissionCandidates { get; set; }
        public DbSet<Vacation> Vacations { get; set; }
        public DbSet<User> Users { get; set; }
        //public DbSet<UserToken> UserTokens { get; set; }
    }
}
