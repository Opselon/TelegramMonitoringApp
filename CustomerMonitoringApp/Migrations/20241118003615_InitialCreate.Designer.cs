﻿// <auto-generated />
using System;
using CustomerMonitoringApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace CustomerMonitoringApp.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20241118003615_InitialCreate")]
    partial class InitialCreate
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.10")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("CustomerMonitoringApp.Domain.Entities.CallHistory", b =>
                {
                    b.Property<int>("CallId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("CallId"));

                    b.Property<string>("CallDateTime")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("CallType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<string>("DestinationPhoneNumber")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<int>("Duration")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasDefaultValue(0);

                    b.Property<string>("FileName")
                        .IsRequired()
                        .HasMaxLength(180)
                        .HasColumnType("nvarchar(180)");

                    b.Property<string>("SourcePhoneNumber")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.HasKey("CallId");

                    b.HasIndex("DestinationPhoneNumber");

                    b.HasIndex("SourcePhoneNumber");

                    b.ToTable("CallHistories");
                });

            modelBuilder.Entity("CustomerMonitoringApp.Domain.Entities.User", b =>
                {
                    b.Property<int>("UserId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("UserId"));

                    b.Property<string>("UserAddressFile")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserBirthDayFile")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserDescriptionFile")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserFamilyFile")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserFatherNameFile")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserNameFile")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserNameProfile")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserNumberFile")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("UserSourceFile")
                        .HasColumnType("nvarchar(max)");

                    b.Property<long?>("UserTelegramID")
                        .HasColumnType("bigint");

                    b.HasKey("UserId");

                    b.HasIndex("UserNumberFile");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("CustomerMonitoringApp.Domain.Views.CallHistoryWithUserNames", b =>
                {
                    b.Property<int>("CallId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("CallId"));

                    b.Property<string>("CallDateTime")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("CallType")
                        .IsRequired()
                        .HasMaxLength(15)
                        .HasColumnType("nvarchar(15)");

                    b.Property<string>("CallerName")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("DestinationPhoneNumber")
                        .IsRequired()
                        .HasMaxLength(13)
                        .HasColumnType("nvarchar(13)");

                    b.Property<int>("Duration")
                        .HasColumnType("int");

                    b.Property<string>("FileName")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("ReceiverName")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("SourcePhoneNumber")
                        .IsRequired()
                        .HasMaxLength(13)
                        .HasColumnType("nvarchar(13)");

                    b.HasKey("CallId");

                    b.HasIndex("DestinationPhoneNumber");

                    b.HasIndex("SourcePhoneNumber");

                    b.ToTable("CallHistoryWithUserNames");
                });

            modelBuilder.Entity("UserPermission", b =>
                {
                    b.Property<int>("PermissionId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("PermissionId"));

                    b.Property<string>("PermissionDescription")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("PermissionType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.Property<int>("UserTelegramID")
                        .HasColumnType("int");

                    b.HasKey("PermissionId");

                    b.HasIndex("UserId");

                    b.ToTable("UserPermissions");
                });

            modelBuilder.Entity("UserPermission", b =>
                {
                    b.HasOne("CustomerMonitoringApp.Domain.Entities.User", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });
#pragma warning restore 612, 618
        }
    }
}