﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApplicationServices;
using AudibleUtilities;
using LibationFileManager;
using LibationWinForms.Dialogs;

namespace LibationWinForms
{
	// this is for manual scan/import. Unrelated to auto-scan
    public partial class Form1
	{

		private ToolStripButton removeCheckedBtn = new();
		private void Configure_ScanManual()
        {
			this.Load += refreshImportMenu;
			AccountsSettingsPersister.Saved += refreshImportMenu;

			#region Create and Add Tool Strip Button
			removeCheckedBtn.DisplayStyle = ToolStripItemDisplayStyle.Text;
			removeCheckedBtn.Name = "removeSelectedBtn";
			removeCheckedBtn.Text = "Remove 0 Books";
			removeCheckedBtn.AutoToolTip = false;
			removeCheckedBtn.ToolTipText = "Remove checked books and series\r\nfrom Libation's database.\r\n\r\nThey will remain in your Audible account.";
			removeCheckedBtn.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			removeCheckedBtn.Alignment = ToolStripItemAlignment.Left;
			removeCheckedBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
			removeCheckedBtn.Font = new System.Drawing.Font(removeCheckedBtn.Font, System.Drawing.FontStyle.Bold);
			removeCheckedBtn.Click += (_, _) => productsDisplay.RemoveCheckedBooksAsync();
			removeCheckedBtn.Visible = false;
			statusStrip1.Items.Insert(1, removeCheckedBtn);
			#endregion
		}

		private void refreshImportMenu(object _, EventArgs __)
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var count = persister.AccountsSettings.Accounts.Count;

			autoScanLibraryToolStripMenuItem.Visible = count > 0;

			noAccountsYetAddAccountToolStripMenuItem.Visible = count == 0;
			scanLibraryToolStripMenuItem.Visible = count == 1;
			scanLibraryOfAllAccountsToolStripMenuItem.Visible = count > 1;
			scanLibraryOfSomeAccountsToolStripMenuItem.Visible = count > 1;

			removeLibraryBooksToolStripMenuItem.Visible = count > 0;
			removeSomeAccountsToolStripMenuItem.Visible = count > 1;
			removeAllAccountsToolStripMenuItem.Visible = count > 1;
		}

		private void noAccountsYetAddAccountToolStripMenuItem_Click(object sender, EventArgs e)
		{
			MessageBox.Show("To load your Audible library, come back here to the Import menu after adding your account");
			new AccountsDialog().ShowDialog();
		}

		private async void scanLibraryToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var firstAccount = persister.AccountsSettings.GetAll().FirstOrDefault();
			await scanLibrariesAsync(firstAccount);
		}

		private async void scanLibraryOfAllAccountsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var allAccounts = persister.AccountsSettings.GetAll();
			await scanLibrariesAsync(allAccounts);
		}

		private async void scanLibraryOfSomeAccountsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var scanAccountsDialog = new ScanAccountsDialog();

			if (scanAccountsDialog.ShowDialog() != DialogResult.OK)
				return;

			if (!scanAccountsDialog.CheckedAccounts.Any())
				return;

			await scanLibrariesAsync(scanAccountsDialog.CheckedAccounts);
		}

		private void removeLibraryBooksToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// if 0 accounts, this will not be visible
			// if 1 account, run scanLibrariesRemovedBooks() on this account
			// if multiple accounts, another menu set will open. do not run scanLibrariesRemovedBooks()
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var accounts = persister.AccountsSettings.GetAll();

			if (accounts.Count != 1)
				return;

			var firstAccount = accounts.Single();
			scanLibrariesRemovedBooks(firstAccount);
		}

		// selectively remove books from all accounts
		private void removeAllAccountsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
			var allAccounts = persister.AccountsSettings.GetAll();
			scanLibrariesRemovedBooks(allAccounts.ToArray());
		}

		// selectively remove books from some accounts
		private void removeSomeAccountsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using var scanAccountsDialog = new ScanAccountsDialog();

			if (scanAccountsDialog.ShowDialog() != DialogResult.OK)
				return;

			if (!scanAccountsDialog.CheckedAccounts.Any())
				return;

			scanLibrariesRemovedBooks(scanAccountsDialog.CheckedAccounts.ToArray());
		}

		private async void scanLibrariesRemovedBooks(params Account[] accounts)
		{
			//This action is meant to operate on the entire library.
			//For removing books within a filter set, use
			//Visible Books > Remove from library
			filterSearchTb.Enabled = false;
			productsDisplay.Filter(null);

			removeCheckedBtn.Visible = true;
			closeRemoveBooksColumnToolStripMenuItem.Visible = true;
			await productsDisplay.ScanAndRemoveBooksAsync(accounts);
		}

		private void closeRemoveBooksColumnToolStripMenuItem_Click(object sender, EventArgs e)
		{
			removeCheckedBtn.Visible = false;
			closeRemoveBooksColumnToolStripMenuItem.Visible = false;
			productsDisplay.CloseRemoveBooksColumn();

			//Restore the filter
			filterSearchTb.Enabled = true;
			performFilter(filterSearchTb.Text);
		}

		private void productsDisplay_RemovableCountChanged(object sender, int removeCount)
		{
			removeCheckedBtn.Text = removeCount switch
			{
				1 => "Remove 1 Book",
				_ => $"Remove {removeCount} Books"
			};
		}

		private async Task scanLibrariesAsync(IEnumerable<Account> accounts) => await scanLibrariesAsync(accounts.ToArray());
		private async Task scanLibrariesAsync(params Account[] accounts)
		{
			try
			{
				var (totalProcessed, newAdded) = await LibraryCommands.ImportAccountAsync(Login.WinformLoginChoiceEager.ApiExtendedFunc, accounts);

				// this is here instead of ScanEnd so that the following is only possible when it's user-initiated, not automatic loop
				if (Configuration.Instance.ShowImportedStats && newAdded > 0)
					MessageBox.Show($"Total processed: {totalProcessed}\r\nNew: {newAdded}");
			}
			catch (Exception ex)
			{
				MessageBoxLib.ShowAdminAlert(
					this,
					"Error importing library. Please try again. If this still happens after 2 or 3 tries, stop and contact administrator",
					"Error importing library",
					ex);
			}
		}
	}
}
