﻿using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LibationFileManager;
using LibationAvalonia.Views;
using System;
using Avalonia.Platform;
using LibationAvalonia.Dialogs;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using ApplicationServices;
using Dinah.Core;

namespace LibationAvalonia
{
	public class App : Application
	{
		public static readonly bool IsWindows;
		public static readonly bool IsLinux;
		public static readonly bool IsMacOs;
		static App()
		{
			IsWindows = OperatingSystem.IsWindows();
			IsLinux = OperatingSystem.IsLinux();
			IsMacOs = OperatingSystem.IsMacOS();
		}

		public static IBrush ProcessQueueBookFailedBrush { get; private set; }
		public static IBrush ProcessQueueBookCompletedBrush { get; private set; }
		public static IBrush ProcessQueueBookCancelledBrush { get; private set; }
		public static IBrush ProcessQueueBookDefaultBrush { get; private set; }
		public static IBrush SeriesEntryGridBackgroundBrush { get; private set; }

		public static IAssetLoader AssetLoader { get; private set; }

		public static readonly Uri AssetUriBase = new("avares://Libation/Assets/");
		public static Stream OpenAsset(string assetRelativePath)
			=> AssetLoader.Open(new Uri(AssetUriBase, assetRelativePath));


		public static bool GoToFile(string path)
			=> IsWindows ? Go.To.File(path)
			: GoToFolder(path is null ? string.Empty : Path.GetDirectoryName(path));

		public static bool GoToFolder(string path)
		{
			if (IsWindows)
				return Go.To.Folder(path);
			else if (IsLinux)
			{
				var startInfo = new System.Diagnostics.ProcessStartInfo()
				{
					FileName = "/bin/xdg-open",
					Arguments = path is null ? string.Empty : $"\"{path}\"",
					UseShellExecute = false, //Import in Linux environments
					CreateNoWindow = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				System.Diagnostics.Process.Start(startInfo);
				return true;
			}
			//Don't know how to do this for mac yet
			else return true;
		}

		public override void Initialize()
		{
			AvaloniaXamlLoader.Load(this);
			AssetLoader = AvaloniaLocator.Current.GetService<IAssetLoader>();
		}

		public static Task<List<DataLayer.LibraryBook>> LibraryTask;
		public static bool SetupRequired;

		public override void OnFrameworkInitializationCompleted()
		{
			LoadStyles();

			var SEGOEUI = new Typeface(new FontFamily(new Uri("avares://Libation/Assets/WINGDING.TTF"), "SEGOEUI_Local"));
			var gtf = FontManager.Current.GetOrAddGlyphTypeface(SEGOEUI);


			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				if (SetupRequired)
				{
					var config = Configuration.Instance;

					var defaultLibationFilesDir = Configuration.UserProfile;

					// check for existing settings in default location
					var defaultSettingsFile = Path.Combine(defaultLibationFilesDir, "Settings.json");
					if (Configuration.SettingsFileIsValid(defaultSettingsFile))
						config.SetLibationFiles(defaultLibationFilesDir);

					if (config.LibationSettingsAreValid)
					{
						LibraryTask = Task.Run(() => DbContexts.GetLibrary_Flat_NoTracking(includeParents: true));
						ShowMainWindow(desktop);
					}
					else
					{
						var setupDialog = new SetupDialog { Config = config };
						setupDialog.Closing += Setup_Closing;
						desktop.MainWindow = setupDialog;
					}
				}
				else
					ShowMainWindow(desktop);
			}

			base.OnFrameworkInitializationCompleted();
		}

		private async void Setup_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			var setupDialog = sender as SetupDialog;
			var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

			try
			{
				// all returns should be preceded by either:
				// - if config.LibationSettingsAreValid
				// - error message, Exit()

				if ((!setupDialog.IsNewUser
					&& !setupDialog.IsReturningUser) ||
					!await RunInstall(setupDialog))
				{
					await CancelInstallation();
					return;
				}


				// most migrations go in here
				AppScaffolding.LibationScaffolding.RunPostConfigMigrations(setupDialog.Config);

				await MessageBox.VerboseLoggingWarning_ShowIfTrue();

#if !DEBUG
				//AutoUpdater.NET only works for WinForms or WPF application projects.
				//checkForUpdate();
#endif
				// logging is init'd here
				AppScaffolding.LibationScaffolding.RunPostMigrationScaffolding(setupDialog.Config);

			}
			catch (Exception ex)
			{
				var title = "Fatal error, pre-logging";
				var body = "An unrecoverable error occurred. Since this error happened before logging could be initialized, this error can not be written to the log file.";
				try
				{
					await MessageBox.ShowAdminAlert(null, body, title, ex);
				}
				catch
				{
					await MessageBox.Show($"{body}\r\n\r\n{ex.Message}\r\n\r\n{ex.StackTrace}", title, MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
				return;
			}

			LibraryTask = Task.Run(() => DbContexts.GetLibrary_Flat_NoTracking(includeParents: true));
			AudibleUtilities.AudibleApiStorage.EnsureAccountsSettingsFileExists();
			ShowMainWindow(desktop);
		}

		private static async Task<bool> RunInstall(SetupDialog setupDialog)
		{
			var config = setupDialog.Config;

			if (setupDialog.IsNewUser)
			{
				config.SetLibationFiles(Configuration.UserProfile);
			}
			else if (setupDialog.IsReturningUser)
			{

				var libationFilesDialog = new LibationFilesDialog();

				if (await libationFilesDialog.ShowDialog<DialogResult>(setupDialog) != DialogResult.OK)
					return false;

				config.SetLibationFiles(libationFilesDialog.SelectedDirectory);
				if (config.LibationSettingsAreValid)
					return true;

				// path did not result in valid settings
				var continueResult = await MessageBox.Show(
					$"No valid settings were found at this location.\r\nWould you like to create a new install settings in this folder?\r\n\r\n{libationFilesDialog.SelectedDirectory}",
					"New install?",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Question);

				if (continueResult != DialogResult.Yes)
					return false;
			}

			// INIT DEFAULT SETTINGS
			// if 'new user' was clicked, or if 'returning user' chose new install: show basic settings dialog
			config.Books ??= Path.Combine(Configuration.UserProfile, "Books");

			AppScaffolding.LibationScaffolding.PopulateMissingConfigValues(config);
			return await new SettingsDialog().ShowDialog<DialogResult>(setupDialog) == DialogResult.OK
				&& config.LibationSettingsAreValid;
		}

		static async Task CancelInstallation()
		{
			await MessageBox.Show("Initial set up cancelled.", "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			Environment.Exit(0);
		}

		private static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
		{
			var mainWindow = new MainWindow();
			desktop.MainWindow = mainWindow;
			mainWindow.RestoreSizeAndLocation(Configuration.Instance);
			mainWindow.OnLoad();
			mainWindow.OnLibraryLoaded(LibraryTask.GetAwaiter().GetResult());
			mainWindow.Show();
		}

		private static void LoadStyles()
		{
			ProcessQueueBookFailedBrush = AvaloniaUtils.GetBrushFromResources("ProcessQueueBookFailedBrush");
			ProcessQueueBookCompletedBrush = AvaloniaUtils.GetBrushFromResources("ProcessQueueBookCompletedBrush");
			ProcessQueueBookCancelledBrush = AvaloniaUtils.GetBrushFromResources("ProcessQueueBookCancelledBrush");
			ProcessQueueBookDefaultBrush = AvaloniaUtils.GetBrushFromResources("ProcessQueueBookDefaultBrush");
			SeriesEntryGridBackgroundBrush = AvaloniaUtils.GetBrushFromResources("SeriesEntryGridBackgroundBrush");
		}
	}
}