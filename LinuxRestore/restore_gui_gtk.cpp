// LinuxRestore/restore_gui_gtk.cpp
// Graphical User Interface using GTK+ 3.0 - Version 4.7.3.0
// Enhanced with 3-step wizard matching Windows RestoreWindowNew:
// - Step 3 uses a two-panel layout (options left, full-height target tree right)
// - Target tree shows ALL disks including boot disk (greyed/insensitive)
// - Toolbar: Refresh, Expand All, Collapse All, Show Hidden Partitions
// - Boot disk cannot be selected; selecting it shows an error and deselects
// - Hidden partitions (EFI, swap, no-mount) hidden by default, toggle to show

#include <gtk/gtk.h>
#include <string>
#include <vector>
#include <functional>
#include "restore_engine.cpp"

class RestoreGUI {
private:
    GtkWidget *window;
    GtkWidget *stack;  // For wizard steps
    GtkWidget *statusBar;

    // Step 1 widgets
    GtkWidget *txtBackupPath;
    GtkWidget *btnBrowseBackup;
    GtkWidget *btnLoadDates;
    GtkWidget *treeViewDates;

    // Step 2 widgets
    GtkWidget *treeViewItems;
    GtkWidget *btnExpandAll;
    GtkWidget *btnCollapseAll;

    // Step 3 widgets — left panel (options)
    GtkWidget *radioOriginal;
    GtkWidget *radioNew;
    GtkWidget *radioDiskTarget;
    GtkWidget *txtDestPath;
    GtkWidget *btnBrowseDest;
    GtkWidget *chkSaveLog;
    GtkWidget *txtLogPath;
    GtkWidget *btnBrowseLog;
    GtkWidget *chkOverwrite;
    GtkWidget *chkPreservePerms;
    GtkWidget *lblDiskMappingNote;
    GtkWidget *lblSelectedTarget;      // shows currently selected target device

    // Step 3 widgets — right panel (target tree, mirrors Windows restore page)
    GtkWidget *treeViewTargetDisks;    // disk/partition target tree
    GtkWidget *chkShowHiddenPartitions; // toolbar: show hidden partitions toggle
    GtkWidget *lblTargetHelp;          // contextual help text below tree

    // Progress dialog
    GtkWidget *progressDialog;
    GtkWidget *progressBar;
    GtkWidget *lblProgress;
    GtkWidget *lblCurrentItem;

    RestoreEngine engine;

    std::string selectedBackupPath;
    std::string selectedTargetDevice;   // device chosen in the disk target tree
    std::vector<RestoreEngine::BackupDate> backupDates;
    std::vector<RestoreEngine::RestoreItem> restoreTree;

    int currentStep = 0; // 0=Step1, 1=Step2, 2=Step3

    // -----------------------------------------------------------------------
    // Target tree column indices
    // Col 0: display text (G_TYPE_STRING)
    // Col 1: size       (G_TYPE_STRING)
    // Col 2: isBootDisk (G_TYPE_BOOLEAN) — drives insensitive/greyed rendering
    // Col 3: device path e.g. /dev/sda  (G_TYPE_STRING)
    // Col 4: isHidden   (G_TYPE_BOOLEAN) — controls row visibility
    // -----------------------------------------------------------------------

    void ensurePasswordForSelectedBackup() {
        std::ifstream file(selectedBackupPath, std::ios::binary);
        if (!file) return;

        char header[7] = { 0 };
        file.read(header, sizeof(header));
        if (file.gcount() == sizeof(header) &&
            std::strncmp(header, "SSBAES1", sizeof(header)) == 0) {
            GtkWidget* dialog = gtk_dialog_new_with_buttons(
                "Encrypted Backup Password",
                GTK_WINDOW(window),
                GTK_DIALOG_MODAL,
                "_Cancel", GTK_RESPONSE_CANCEL,
                "_OK",     GTK_RESPONSE_OK,
                NULL);

            GtkWidget* content = gtk_dialog_get_content_area(GTK_DIALOG(dialog));
            GtkWidget* label = gtk_label_new("Enter the password for this encrypted backup:");
            GtkWidget* entry = gtk_entry_new();
            gtk_entry_set_visibility(GTK_ENTRY(entry), FALSE);
            gtk_box_pack_start(GTK_BOX(content), label, FALSE, FALSE, 5);
            gtk_box_pack_start(GTK_BOX(content), entry, FALSE, FALSE, 5);
            gtk_widget_show_all(dialog);

            if (gtk_dialog_run(GTK_DIALOG(dialog)) == GTK_RESPONSE_OK) {
                const char* password = gtk_entry_get_text(GTK_ENTRY(entry));
                engine.SetBackupPassword(password ? password : "");
            }
            gtk_widget_destroy(dialog);
        }
    }

    // -----------------------------------------------------------------------
    // Target tree helpers
    // -----------------------------------------------------------------------

    bool isShowingHiddenPartitions() const {
        return gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(chkShowHiddenPartitions));
    }

    void applyHiddenFilter(GtkTreeStore* store) {
        // Walk the store and update row visibility via the hidden column.
        // GTK GtkTreeStore doesn't have built-in row filtering with toggle, so we
        // use a GtkTreeModelFilter driven by a custom visible function. However,
        // since we rebuild the store on refresh anyway, we simply rebuild here.
        // See loadTargetDisks() which is called by the toolbar Refresh button.
        (void)store;
    }

    // Rebuild the entire target disk tree. Called on load, Refresh, and
    // Show-Hidden-Partitions toggle.
    void loadTargetDisks() {
        auto disks = engine.ListTargetDisks();
        bool showHidden = isShowingHiddenPartitions();

        GtkTreeStore* store = GTK_TREE_STORE(
            gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewTargetDisks)));
        gtk_tree_store_clear(store);
        selectedTargetDevice.clear();
        gtk_label_set_text(GTK_LABEL(lblSelectedTarget),
            "No target selected — click a disk or partition above");

        for (const auto& d : disks) {
            GtkTreeIter diskIter;
            gtk_tree_store_append(store, &diskIter, NULL);

            std::string diskLabel = "\U0001f4be  /dev/" + d.device + "  [Disk]  " + d.size;
            if (d.isBootDisk) diskLabel += "  \u26a0 [Boot/System — cannot restore]";

            gtk_tree_store_set(store, &diskIter,
                0, diskLabel.c_str(),
                1, d.size.c_str(),
                2, (gboolean)d.isBootDisk,
                3, ("/dev/" + d.device).c_str(),
                4, (gboolean)FALSE,   // disks are never hidden
                -1);

            for (const auto& p : d.partitions) {
                if (!showHidden && p.isHiddenPartition) continue;

                GtkTreeIter partIter;
                gtk_tree_store_append(store, &partIter, &diskIter);

                std::string partLabel = "    \U0001f4c2  /dev/" + p.device + "  " + p.size;
                if (!p.fsType.empty())     partLabel += "  [" + p.fsType + "]";
                if (!p.mountPoint.empty()) partLabel += "  " + p.mountPoint;
                if (d.isBootDisk)          partLabel += "  (boot)";
                if (p.isHiddenPartition)   partLabel += "  \U0001f441 [hidden]";

                gtk_tree_store_set(store, &partIter,
                    0, partLabel.c_str(),
                    1, p.size.c_str(),
                    2, (gboolean)d.isBootDisk,
                    3, ("/dev/" + p.device).c_str(),
                    4, (gboolean)p.isHiddenPartition,
                    -1);
            }
        }

        gtk_tree_view_expand_all(GTK_TREE_VIEW(treeViewTargetDisks));
        updateTargetHelpText();
        updateStatusBar("Target disks loaded — select a disk or partition to restore to");
    }

    // Update the contextual help label below the target tree based on which
    // restore radio is active, mirroring Windows txtDriveTreeHelp.
    void updateTargetHelpText() {
        const char* msg = "";
        if (gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(radioDiskTarget))) {
            msg = "Select a disk or partition to use as the restore target.\n"
                  "The boot/system disk (shown greyed) cannot be selected.";
        } else if (gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(radioNew))) {
            msg = "Browse to an alternate folder on the selected partition.\n"
                  "Choose a partition, then click Browse.";
        } else {
            msg = "Files will be restored to their original paths.\n"
                  "The target tree is shown for reference only.";
        }
        gtk_label_set_text(GTK_LABEL(lblTargetHelp), msg);
    }

    // -----------------------------------------------------------------------
    // Cell rendering: grey-out boot-disk rows
    // -----------------------------------------------------------------------
    static void cellDataFuncText(GtkTreeViewColumn* /*col*/,
                                  GtkCellRenderer* renderer,
                                  GtkTreeModel* model,
                                  GtkTreeIter* iter,
                                  gpointer /*data*/) {
        gboolean isBootDisk;
        gtk_tree_model_get(model, iter, 2, &isBootDisk, -1);
        if (isBootDisk) {
            g_object_set(renderer,
                "foreground", "gray",
                "foreground-set", TRUE,
                "style", PANGO_STYLE_ITALIC,
                "style-set", TRUE,
                NULL);
        } else {
            g_object_set(renderer,
                "foreground-set", FALSE,
                "style-set", FALSE,
                NULL);
        }
    }

    // -----------------------------------------------------------------------
    // Callbacks
    // -----------------------------------------------------------------------

    static void onBrowseBackup(GtkWidget*, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        GtkWidget *dialog = gtk_file_chooser_dialog_new(
            "Select Backup Folder", GTK_WINDOW(gui->window),
            GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER,
            "_Cancel", GTK_RESPONSE_CANCEL,
            "_Open",   GTK_RESPONSE_ACCEPT,
            NULL);
        if (gtk_dialog_run(GTK_DIALOG(dialog)) == GTK_RESPONSE_ACCEPT) {
            char *filename = gtk_file_chooser_get_filename(GTK_FILE_CHOOSER(dialog));
            gtk_entry_set_text(GTK_ENTRY(gui->txtBackupPath), filename);
            g_free(filename);
        }
        gtk_widget_destroy(dialog);
    }

    static void onLoadDates(GtkWidget*, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        const char* path = gtk_entry_get_text(GTK_ENTRY(gui->txtBackupPath));
        if (strlen(path) == 0) { gui->showError("Please select a backup folder"); return; }
        gui->loadBackupDates(path);
    }

    static void onStep1Next(GtkWidget*, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        GtkTreeSelection* selection = gtk_tree_view_get_selection(GTK_TREE_VIEW(gui->treeViewDates));
        if (gtk_tree_selection_count_selected_rows(selection) == 0) {
            gui->showError("Please select a backup date"); return;
        }
        GtkTreeIter iter; GtkTreeModel* model;
        gtk_tree_selection_get_selected(selection, &model, &iter);
        gchar* path; gtk_tree_model_get(model, &iter, 3, &path, -1);
        gui->selectedBackupPath = path; g_free(path);
        gui->ensurePasswordForSelectedBackup();
        gui->loadBackupContents();
        gtk_stack_set_visible_child_name(GTK_STACK(gui->stack), "step2");
        gui->currentStep = 1;
        gui->updateStatusBar("Step 2: Select Items to Restore");
    }

    static void onStep2Back(GtkWidget*, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gtk_stack_set_visible_child_name(GTK_STACK(gui->stack), "step1");
        gui->currentStep = 0;
        gui->updateStatusBar("Step 1: Select Backup & Date");
    }

    static void onStep2Next(GtkWidget*, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        if (!gui->hasCheckedItems()) {
            gui->showError("Please select at least one item to restore"); return;
        }
        gui->loadTargetDisks();
        gtk_stack_set_visible_child_name(GTK_STACK(gui->stack), "step3");
        gui->currentStep = 2;
        gui->updateStatusBar("Step 3: Select Restore Destination");
    }

    static void onStep3Back(GtkWidget*, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gtk_stack_set_visible_child_name(GTK_STACK(gui->stack), "step2");
        gui->currentStep = 1;
        gui->updateStatusBar("Step 2: Select Items to Restore");
    }

    static void onBrowseDest(GtkWidget*, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        GtkWidget *dialog = gtk_file_chooser_dialog_new(
            "Select Restore Destination", GTK_WINDOW(gui->window),
            GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER,
            "_Cancel", GTK_RESPONSE_CANCEL,
            "_Select", GTK_RESPONSE_ACCEPT,
            NULL);
        if (gtk_dialog_run(GTK_DIALOG(dialog)) == GTK_RESPONSE_ACCEPT) {
            char *filename = gtk_file_chooser_get_filename(GTK_FILE_CHOOSER(dialog));
            gtk_entry_set_text(GTK_ENTRY(gui->txtDestPath), filename);
            g_free(filename);
        }
        gtk_widget_destroy(dialog);
    }

    static void onStartRestore(GtkWidget*, gpointer data) {
        static_cast<RestoreGUI*>(data)->performRestore();
    }

    static void onExpandAll(GtkWidget*, gpointer data) {
        gtk_tree_view_expand_all(GTK_TREE_VIEW(
            static_cast<RestoreGUI*>(data)->treeViewItems));
    }

    static void onCollapseAll(GtkWidget*, gpointer data) {
        gtk_tree_view_collapse_all(GTK_TREE_VIEW(
            static_cast<RestoreGUI*>(data)->treeViewItems));
    }

    // Toolbar: Refresh target tree
    static void onRefreshTarget(GtkWidget*, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gui->loadTargetDisks();
    }

    // Toolbar: Expand all target tree nodes
    static void onExpandAllTarget(GtkWidget*, gpointer data) {
        gtk_tree_view_expand_all(GTK_TREE_VIEW(
            static_cast<RestoreGUI*>(data)->treeViewTargetDisks));
    }

    // Toolbar: Collapse all target tree nodes
    static void onCollapseAllTarget(GtkWidget*, gpointer data) {
        gtk_tree_view_collapse_all(GTK_TREE_VIEW(
            static_cast<RestoreGUI*>(data)->treeViewTargetDisks));
    }

    // Toolbar: Show Hidden Partitions toggled
    static void onShowHiddenPartitions(GtkWidget*, gpointer data) {
        static_cast<RestoreGUI*>(data)->loadTargetDisks();
    }

    static void onCellToggled(GtkCellRendererToggle *renderer, gchar *path_str, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        GtkTreeModel *model = gtk_tree_view_get_model(GTK_TREE_VIEW(gui->treeViewItems));
        GtkTreePath *path = gtk_tree_path_new_from_string(path_str);
        GtkTreeIter iter;
        gtk_tree_model_get_iter(model, &iter, path);
        gboolean checked;
        gtk_tree_model_get(model, &iter, 0, &checked, -1);
        gtk_tree_store_set(GTK_TREE_STORE(model), &iter, 0, !checked, -1);
        gtk_tree_path_free(path);
    }

    static void onDestLocationChanged(GtkToggleButton *button, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        if (!gtk_toggle_button_get_active(button)) return;

        gboolean newLocation = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(gui->radioNew));
        gtk_widget_set_sensitive(gui->txtDestPath,   newLocation);
        gtk_widget_set_sensitive(gui->btnBrowseDest, newLocation);

        gboolean diskRecon = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(gui->radioDiskTarget));
        gtk_widget_set_visible(gui->lblDiskMappingNote, diskRecon);

        gui->updateTargetHelpText();
    }

    // Target disk selection: single-click in the target tree
    static void onTargetDiskSelected(GtkTreeSelection *selection, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);

        GtkTreeIter iter; GtkTreeModel* model;
        if (!gtk_tree_selection_get_selected(selection, &model, &iter)) return;

        gboolean isBootDisk;
        gtk_tree_model_get(model, &iter, 2, &isBootDisk, -1);

        if (isBootDisk) {
            gui->showError(
                "The boot/system disk cannot be used as a restore target.\n"
                "Please select a different disk or partition.");
            gtk_tree_selection_unselect_all(selection);
            return;
        }

        gchar* devicePath;
        gtk_tree_model_get(model, &iter, 3, &devicePath, -1);
        gui->selectedTargetDevice = devicePath ? devicePath : "";
        g_free(devicePath);

        std::string msg = "Selected restore target: " + gui->selectedTargetDevice;
        gtk_label_set_text(GTK_LABEL(gui->lblSelectedTarget), msg.c_str());

        // Auto-switch to "Restore to target disk" mode to match Windows behavior
        // of auto-wiring the destination when a target is chosen from the tree.
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(gui->radioDiskTarget), TRUE);
    }

    static gboolean onDelete(GtkWidget*, GdkEvent*, gpointer) {
        gtk_main_quit();
        return FALSE;
    }

    // -----------------------------------------------------------------------
    // Data loading helpers
    // -----------------------------------------------------------------------

    void loadBackupDates(const std::string& backupPath) {
        backupDates = engine.EnumerateBackupDates(backupPath);
        GtkTreeStore* store = GTK_TREE_STORE(
            gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewDates)));
        gtk_tree_store_clear(store);
        if (backupDates.empty()) { showError("No backups found in the selected folder"); return; }
        for (const auto& date : backupDates) {
            GtkTreeIter iter;
            gtk_tree_store_append(store, &iter, NULL);
            gtk_tree_store_set(store, &iter,
                0, date.date.c_str(), 1, date.type.c_str(),
                2, date.size.c_str(), 3, date.path.c_str(), -1);
        }
        updateStatusBar("Found " + std::to_string(backupDates.size()) + " backup(s)");
    }

    void loadBackupContents() {
        restoreTree = engine.BuildRestoreTree(selectedBackupPath);
        GtkTreeStore* store = GTK_TREE_STORE(
            gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewItems)));
        gtk_tree_store_clear(store);
        if (restoreTree.empty()) { showError("Failed to load backup contents"); return; }
        for (const auto& item : restoreTree) addTreeItem(store, NULL, item);
        updateStatusBar("Backup contents loaded");
    }

    void addTreeItem(GtkTreeStore* store, GtkTreeIter* parent,
                     const RestoreEngine::RestoreItem& item) {
        GtkTreeIter iter;
        gtk_tree_store_append(store, &iter, parent);
        gtk_tree_store_set(store, &iter,
            0, FALSE, 1, item.name.c_str(), 2, item.type.c_str(), 3, item.path.c_str(), -1);
        for (const auto& child : item.children) addTreeItem(store, &iter, child);
    }

    bool hasCheckedItems() {
        GtkTreeModel* model = gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewItems));
        return hasCheckedItemsRecursive(model, NULL);
    }

    bool hasCheckedItemsRecursive(GtkTreeModel* model, GtkTreeIter* parent) {
        GtkTreeIter iter;
        gboolean valid = parent ? gtk_tree_model_iter_children(model, &iter, parent)
                                : gtk_tree_model_get_iter_first(model, &iter);
        while (valid) {
            gboolean checked; gtk_tree_model_get(model, &iter, 0, &checked, -1);
            if (checked) return true;
            if (hasCheckedItemsRecursive(model, &iter)) return true;
            valid = gtk_tree_model_iter_next(model, &iter);
        }
        return false;
    }

    void collectCheckedItems(std::vector<std::string>& items) {
        GtkTreeModel* model = gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewItems));
        collectCheckedItemsRecursive(model, NULL, items);
    }

    void collectCheckedItemsRecursive(GtkTreeModel* model, GtkTreeIter* parent,
                                      std::vector<std::string>& items) {
        GtkTreeIter iter;
        gboolean valid = parent ? gtk_tree_model_iter_children(model, &iter, parent)
                                : gtk_tree_model_get_iter_first(model, &iter);
        while (valid) {
            gboolean checked; gchar* path;
            gtk_tree_model_get(model, &iter, 0, &checked, 3, &path, -1);
            if (checked) items.push_back(path);
            else collectCheckedItemsRecursive(model, &iter, items);
            g_free(path);
            valid = gtk_tree_model_iter_next(model, &iter);
        }
    }

    // -----------------------------------------------------------------------
    // Restore execution
    // -----------------------------------------------------------------------

    void performRestore() {
        bool newLocation  = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(radioNew));
        bool diskTarget   = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(radioDiskTarget));
        std::string dest;

        if (newLocation) {
            dest = gtk_entry_get_text(GTK_ENTRY(txtDestPath));
            if (dest.empty()) { showError("Please select a restore destination"); return; }
        } else if (diskTarget) {
            if (selectedTargetDevice.empty()) {
                showError("Please select a target disk or partition from the list"); return;
            }
            dest = selectedTargetDevice;
        }

        std::vector<std::string> items;
        collectCheckedItems(items);
        if (items.empty()) { showError("No items selected for restore"); return; }

        GtkWidget *dlg = gtk_message_dialog_new(GTK_WINDOW(window),
            GTK_DIALOG_MODAL, GTK_MESSAGE_QUESTION, GTK_BUTTONS_YES_NO,
            "Are you sure you want to restore %d item(s)?\n\nTarget: %s",
            (int)items.size(),
            dest.empty() ? "original locations" : dest.c_str());
        int response = gtk_dialog_run(GTK_DIALOG(dlg));
        gtk_widget_destroy(dlg);
        if (response != GTK_RESPONSE_YES) return;

        showProgressDialog();

        bool overwrite = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(chkOverwrite));
        auto callback = [this](int percent, const std::string& msg) {
            gtk_progress_bar_set_fraction(GTK_PROGRESS_BAR(progressBar), percent / 100.0);
            gtk_label_set_text(GTK_LABEL(lblProgress), msg.c_str());
            while (gtk_events_pending()) gtk_main_iteration();
        };

        bool success = engine.RestoreWithManifest(
            selectedBackupPath, dest, items, overwrite, callback);
        gtk_widget_destroy(progressDialog);

        if (success) showInfo("Restore completed successfully!");
        else showError("Restore failed: " + engine.GetLastError());
    }

    void showProgressDialog() {
        progressDialog = gtk_window_new(GTK_WINDOW_TOPLEVEL);
        gtk_window_set_title(GTK_WINDOW(progressDialog), "Restoring...");
        gtk_window_set_modal(GTK_WINDOW(progressDialog), TRUE);
        gtk_window_set_transient_for(GTK_WINDOW(progressDialog), GTK_WINDOW(window));
        gtk_window_set_default_size(GTK_WINDOW(progressDialog), 450, 110);

        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
        gtk_container_set_border_width(GTK_CONTAINER(vbox), 20);
        gtk_container_add(GTK_CONTAINER(progressDialog), vbox);

        lblProgress = gtk_label_new("Starting restore...");
        gtk_label_set_xalign(GTK_LABEL(lblProgress), 0.0f);
        gtk_box_pack_start(GTK_BOX(vbox), lblProgress, FALSE, FALSE, 0);

        progressBar = gtk_progress_bar_new();
        gtk_box_pack_start(GTK_BOX(vbox), progressBar, FALSE, FALSE, 0);

        gtk_widget_show_all(progressDialog);
    }

    void showError(const std::string& message) {
        GtkWidget *dlg = gtk_message_dialog_new(GTK_WINDOW(window),
            GTK_DIALOG_MODAL, GTK_MESSAGE_ERROR, GTK_BUTTONS_OK, "%s", message.c_str());
        gtk_dialog_run(GTK_DIALOG(dlg));
        gtk_widget_destroy(dlg);
    }

    void showInfo(const std::string& message) {
        GtkWidget *dlg = gtk_message_dialog_new(GTK_WINDOW(window),
            GTK_DIALOG_MODAL, GTK_MESSAGE_INFO, GTK_BUTTONS_OK, "%s", message.c_str());
        gtk_dialog_run(GTK_DIALOG(dlg));
        gtk_widget_destroy(dlg);
    }

    void updateStatusBar(const std::string& message) {
        gtk_statusbar_push(GTK_STATUSBAR(statusBar), 0, message.c_str());
    }

    // -----------------------------------------------------------------------
    // Step widgets
    // -----------------------------------------------------------------------

    GtkWidget* createStep1() {
        GtkWidget *box = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
        gtk_container_set_border_width(GTK_CONTAINER(box), 20);

        GtkWidget *lblTitle = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblTitle), "<b>Step 1: Select Backup and Date</b>");
        gtk_box_pack_start(GTK_BOX(box), lblTitle, FALSE, FALSE, 10);

        GtkWidget *hbox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        gtk_box_pack_start(GTK_BOX(hbox), gtk_label_new("Backup Folder:"), FALSE, FALSE, 0);
        txtBackupPath = gtk_entry_new();
        gtk_box_pack_start(GTK_BOX(hbox), txtBackupPath, TRUE, TRUE, 0);
        btnBrowseBackup = gtk_button_new_with_label("Browse...");
        g_signal_connect(btnBrowseBackup, "clicked", G_CALLBACK(onBrowseBackup), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnBrowseBackup, FALSE, FALSE, 0);
        gtk_box_pack_start(GTK_BOX(box), hbox, FALSE, FALSE, 0);

        btnLoadDates = gtk_button_new_with_label("Load Backup Dates");
        g_signal_connect(btnLoadDates, "clicked", G_CALLBACK(onLoadDates), this);
        gtk_box_pack_start(GTK_BOX(box), btnLoadDates, FALSE, FALSE, 0);

        GtkWidget *scrolled = gtk_scrolled_window_new(NULL, NULL);
        gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scrolled),
            GTK_POLICY_AUTOMATIC, GTK_POLICY_AUTOMATIC);
        gtk_widget_set_vexpand(scrolled, TRUE);

        GtkTreeStore *store = gtk_tree_store_new(4,
            G_TYPE_STRING, G_TYPE_STRING, G_TYPE_STRING, G_TYPE_STRING);
        treeViewDates = gtk_tree_view_new_with_model(GTK_TREE_MODEL(store));
        GtkCellRenderer *r = gtk_cell_renderer_text_new();
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewDates),
            -1, "Date", r, "text", 0, NULL);
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewDates),
            -1, "Type", r, "text", 1, NULL);
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewDates),
            -1, "Size", r, "text", 2, NULL);
        gtk_container_add(GTK_CONTAINER(scrolled), treeViewDates);
        gtk_box_pack_start(GTK_BOX(box), scrolled, TRUE, TRUE, 0);

        GtkWidget *btnNext = gtk_button_new_with_label("Next >");
        g_signal_connect(btnNext, "clicked", G_CALLBACK(onStep1Next), this);
        gtk_box_pack_start(GTK_BOX(box), btnNext, FALSE, FALSE, 0);
        return box;
    }

    GtkWidget* createStep2() {
        GtkWidget *box = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
        gtk_container_set_border_width(GTK_CONTAINER(box), 20);

        GtkWidget *lblTitle = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblTitle), "<b>Step 2: Select Items to Restore</b>");
        gtk_box_pack_start(GTK_BOX(box), lblTitle, FALSE, FALSE, 10);

        GtkWidget *scrolled = gtk_scrolled_window_new(NULL, NULL);
        gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scrolled),
            GTK_POLICY_AUTOMATIC, GTK_POLICY_AUTOMATIC);
        gtk_widget_set_vexpand(scrolled, TRUE);

        GtkTreeStore *store = gtk_tree_store_new(4,
            G_TYPE_BOOLEAN, G_TYPE_STRING, G_TYPE_STRING, G_TYPE_STRING);
        treeViewItems = gtk_tree_view_new_with_model(GTK_TREE_MODEL(store));

        GtkCellRenderer *toggle = gtk_cell_renderer_toggle_new();
        g_signal_connect(toggle, "toggled", G_CALLBACK(onCellToggled), this);
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewItems),
            -1, "", toggle, "active", 0, NULL);
        GtkCellRenderer *text = gtk_cell_renderer_text_new();
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewItems),
            -1, "Name", text, "text", 1, NULL);
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewItems),
            -1, "Type", text, "text", 2, NULL);

        gtk_container_add(GTK_CONTAINER(scrolled), treeViewItems);
        gtk_box_pack_start(GTK_BOX(box), scrolled, TRUE, TRUE, 0);

        GtkWidget *hbox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        btnExpandAll = gtk_button_new_with_label("Expand All");
        g_signal_connect(btnExpandAll, "clicked", G_CALLBACK(onExpandAll), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnExpandAll, FALSE, FALSE, 0);
        btnCollapseAll = gtk_button_new_with_label("Collapse All");
        g_signal_connect(btnCollapseAll, "clicked", G_CALLBACK(onCollapseAll), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnCollapseAll, FALSE, FALSE, 0);
        gtk_box_pack_start(GTK_BOX(box), hbox, FALSE, FALSE, 0);

        GtkWidget *navBox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        GtkWidget *btnBack = gtk_button_new_with_label("< Back");
        g_signal_connect(btnBack, "clicked", G_CALLBACK(onStep2Back), this);
        gtk_box_pack_start(GTK_BOX(navBox), btnBack, FALSE, FALSE, 0);
        GtkWidget *btnNext = gtk_button_new_with_label("Next >");
        g_signal_connect(btnNext, "clicked", G_CALLBACK(onStep2Next), this);
        gtk_box_pack_end(GTK_BOX(navBox), btnNext, FALSE, FALSE, 0);
        gtk_box_pack_start(GTK_BOX(box), navBox, FALSE, FALSE, 0);
        return box;
    }

    // Step 3: two-panel layout — options on the left, full-height target tree
    // on the right — mirroring the Windows RestoreWindowNew two-column design.
    GtkWidget* createStep3() {
        // Outer horizontal paned: left = options, right = target tree
        GtkWidget *paned = gtk_paned_new(GTK_ORIENTATION_HORIZONTAL);
        gtk_paned_set_position(GTK_PANED(paned), 380);

        // ---- LEFT PANEL: restore mode options ---------------------------------
        GtkWidget *leftBox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 8);
        gtk_container_set_border_width(GTK_CONTAINER(leftBox), 14);

        GtkWidget *lblTitle = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblTitle),
            "<b>Step 3: Select Restore Destination</b>");
        gtk_label_set_xalign(GTK_LABEL(lblTitle), 0.0f);
        gtk_box_pack_start(GTK_BOX(leftBox), lblTitle, FALSE, FALSE, 4);

        // Selected target echo label
        lblSelectedTarget = gtk_label_new("No target selected — select a disk or partition on the right");
        gtk_label_set_line_wrap(GTK_LABEL(lblSelectedTarget), TRUE);
        gtk_label_set_xalign(GTK_LABEL(lblSelectedTarget), 0.0f);
        gtk_widget_set_margin_bottom(lblSelectedTarget, 6);
        gtk_box_pack_start(GTK_BOX(leftBox), lblSelectedTarget, FALSE, FALSE, 0);

        // Restore mode radios
        GtkWidget *lblMode = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblMode), "<b>Restore Mode:</b>");
        gtk_label_set_xalign(GTK_LABEL(lblMode), 0.0f);
        gtk_box_pack_start(GTK_BOX(leftBox), lblMode, FALSE, FALSE, 2);

        radioOriginal = gtk_radio_button_new_with_label(NULL,
            "Restore to original location");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(radioOriginal), TRUE);
        g_signal_connect(radioOriginal, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(leftBox), radioOriginal, FALSE, FALSE, 0);

        radioNew = gtk_radio_button_new_with_label_from_widget(
            GTK_RADIO_BUTTON(radioOriginal), "Restore to alternate folder");
        g_signal_connect(radioNew, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(leftBox), radioNew, FALSE, FALSE, 0);

        radioDiskTarget = gtk_radio_button_new_with_label_from_widget(
            GTK_RADIO_BUTTON(radioOriginal), "Restore to selected disk / partition target");
        g_signal_connect(radioDiskTarget, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(leftBox), radioDiskTarget, FALSE, FALSE, 0);

        // Alternate folder row (active only in radioNew mode)
        GtkWidget *hbox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        gtk_box_pack_start(GTK_BOX(hbox), gtk_label_new("Folder:"), FALSE, FALSE, 0);
        txtDestPath = gtk_entry_new();
        gtk_widget_set_sensitive(txtDestPath, FALSE);
        gtk_box_pack_start(GTK_BOX(hbox), txtDestPath, TRUE, TRUE, 0);
        btnBrowseDest = gtk_button_new_with_label("Browse...");
        gtk_widget_set_sensitive(btnBrowseDest, FALSE);
        g_signal_connect(btnBrowseDest, "clicked", G_CALLBACK(onBrowseDest), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnBrowseDest, FALSE, FALSE, 0);
        gtk_box_pack_start(GTK_BOX(leftBox), hbox, FALSE, FALSE, 4);

        // Disk reconstruction note (shown when radioDiskTarget active)
        lblDiskMappingNote = gtk_label_new(
            "The restore engine will reconstruct the partition layout from backup\n"
            "metadata and write all volumes to the selected target disk.");
        gtk_label_set_line_wrap(GTK_LABEL(lblDiskMappingNote), TRUE);
        gtk_label_set_xalign(GTK_LABEL(lblDiskMappingNote), 0.0f);
        gtk_widget_set_visible(lblDiskMappingNote, FALSE);
        gtk_box_pack_start(GTK_BOX(leftBox), lblDiskMappingNote, FALSE, FALSE, 2);

        GtkWidget *sep = gtk_separator_new(GTK_ORIENTATION_HORIZONTAL);
        gtk_box_pack_start(GTK_BOX(leftBox), sep, FALSE, FALSE, 4);

        chkOverwrite = gtk_check_button_new_with_label("Overwrite existing files");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(chkOverwrite), TRUE);
        gtk_box_pack_start(GTK_BOX(leftBox), chkOverwrite, FALSE, FALSE, 0);

        chkPreservePerms = gtk_check_button_new_with_label("Preserve file permissions");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(chkPreservePerms), TRUE);
        gtk_box_pack_start(GTK_BOX(leftBox), chkPreservePerms, FALSE, FALSE, 0);

        // Push nav buttons to the bottom
        GtkWidget *spacer = gtk_box_new(GTK_ORIENTATION_VERTICAL, 0);
        gtk_widget_set_vexpand(spacer, TRUE);
        gtk_box_pack_start(GTK_BOX(leftBox), spacer, TRUE, TRUE, 0);

        GtkWidget *navBox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        GtkWidget *btnBack = gtk_button_new_with_label("< Back");
        g_signal_connect(btnBack, "clicked", G_CALLBACK(onStep3Back), this);
        gtk_box_pack_start(GTK_BOX(navBox), btnBack, FALSE, FALSE, 0);
        GtkWidget *btnRestore = gtk_button_new_with_label("Start Restore");
        g_signal_connect(btnRestore, "clicked", G_CALLBACK(onStartRestore), this);
        gtk_box_pack_end(GTK_BOX(navBox), btnRestore, FALSE, FALSE, 0);
        gtk_box_pack_start(GTK_BOX(leftBox), navBox, FALSE, FALSE, 0);

        gtk_paned_pack1(GTK_PANED(paned), leftBox, FALSE, FALSE);

        // ---- RIGHT PANEL: full-height restore target tree --------------------
        GtkWidget *rightBox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 4);
        gtk_container_set_border_width(GTK_CONTAINER(rightBox), 8);

        GtkWidget *lblHeader = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblHeader),
            "<b>Select Restore Target Disk or Partition:</b>");
        gtk_label_set_xalign(GTK_LABEL(lblHeader), 0.0f);
        gtk_box_pack_start(GTK_BOX(rightBox), lblHeader, FALSE, FALSE, 0);

        // Target tree — store: display, size, isBootDisk, device, isHidden
        GtkTreeStore *diskStore = gtk_tree_store_new(5,
            G_TYPE_STRING, G_TYPE_STRING, G_TYPE_BOOLEAN,
            G_TYPE_STRING, G_TYPE_BOOLEAN);
        treeViewTargetDisks = gtk_tree_view_new_with_model(GTK_TREE_MODEL(diskStore));
        gtk_tree_view_set_headers_visible(GTK_TREE_VIEW(treeViewTargetDisks), TRUE);
        gtk_tree_selection_set_mode(
            gtk_tree_view_get_selection(GTK_TREE_VIEW(treeViewTargetDisks)),
            GTK_SELECTION_SINGLE);

        // Device column — uses a custom cell data function to grey boot rows
        GtkCellRenderer *textRend = gtk_cell_renderer_text_new();
        GtkTreeViewColumn *devCol = gtk_tree_view_column_new_with_attributes(
            "Device / Partition", textRend, "text", 0, NULL);
        gtk_tree_view_column_set_cell_data_func(devCol, textRend, cellDataFuncText, NULL, NULL);
        gtk_tree_view_column_set_expand(devCol, TRUE);
        gtk_tree_view_append_column(GTK_TREE_VIEW(treeViewTargetDisks), devCol);

        GtkCellRenderer *sizeRend = gtk_cell_renderer_text_new();
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewTargetDisks),
            -1, "Size", sizeRend, "text", 1, NULL);

        g_signal_connect(
            gtk_tree_view_get_selection(GTK_TREE_VIEW(treeViewTargetDisks)),
            "changed", G_CALLBACK(onTargetDiskSelected), this);

        GtkWidget *scrolledDisks = gtk_scrolled_window_new(NULL, NULL);
        gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scrolledDisks),
            GTK_POLICY_AUTOMATIC, GTK_POLICY_AUTOMATIC);
        gtk_widget_set_vexpand(scrolledDisks, TRUE);
        gtk_container_add(GTK_CONTAINER(scrolledDisks), treeViewTargetDisks);
        gtk_box_pack_start(GTK_BOX(rightBox), scrolledDisks, TRUE, TRUE, 0);

        // Help text below tree (contextual, updated by updateTargetHelpText)
        lblTargetHelp = gtk_label_new(
            "Click any non-boot disk or partition to select it as the restore target.\n"
            "The boot/system disk is shown greyed and cannot be selected.");
        gtk_label_set_line_wrap(GTK_LABEL(lblTargetHelp), TRUE);
        gtk_label_set_xalign(GTK_LABEL(lblTargetHelp), 0.0f);
        gtk_box_pack_start(GTK_BOX(rightBox), lblTargetHelp, FALSE, FALSE, 2);

        // Bottom toolbar: Refresh | Expand All | Collapse All | Show Hidden Partitions
        GtkWidget *toolbar = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 4);

        GtkWidget *btnRefresh = gtk_button_new_with_label("\u21ba Refresh");
        g_signal_connect(btnRefresh, "clicked", G_CALLBACK(onRefreshTarget), this);
        gtk_box_pack_start(GTK_BOX(toolbar), btnRefresh, FALSE, FALSE, 0);

        GtkWidget *btnExpAll = gtk_button_new_with_label("Expand All");
        g_signal_connect(btnExpAll, "clicked", G_CALLBACK(onExpandAllTarget), this);
        gtk_box_pack_start(GTK_BOX(toolbar), btnExpAll, FALSE, FALSE, 0);

        GtkWidget *btnColAll = gtk_button_new_with_label("Collapse All");
        g_signal_connect(btnColAll, "clicked", G_CALLBACK(onCollapseAllTarget), this);
        gtk_box_pack_start(GTK_BOX(toolbar), btnColAll, FALSE, FALSE, 0);

        chkShowHiddenPartitions = gtk_check_button_new_with_label("Show Hidden Partitions");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(chkShowHiddenPartitions), FALSE);
        g_signal_connect(chkShowHiddenPartitions, "toggled",
            G_CALLBACK(onShowHiddenPartitions), this);
        gtk_box_pack_start(GTK_BOX(toolbar), chkShowHiddenPartitions, FALSE, FALSE, 4);

        gtk_box_pack_start(GTK_BOX(rightBox), toolbar, FALSE, FALSE, 0);

        gtk_paned_pack2(GTK_PANED(paned), rightBox, TRUE, FALSE);

        return paned;
    }

public:
    RestoreGUI() {
        window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
        gtk_window_set_title(GTK_WINDOW(window),
            "Secure Server Backup — Linux Recovery v4.7.3");
        gtk_window_set_default_size(GTK_WINDOW(window), 1000, 680);
        g_signal_connect(window, "delete-event", G_CALLBACK(onDelete), NULL);

        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 0);
        gtk_container_add(GTK_CONTAINER(window), vbox);

        stack = gtk_stack_new();
        gtk_stack_set_transition_type(GTK_STACK(stack),
            GTK_STACK_TRANSITION_TYPE_SLIDE_LEFT_RIGHT);
        gtk_widget_set_vexpand(stack, TRUE);

        gtk_stack_add_named(GTK_STACK(stack), createStep1(), "step1");
        gtk_stack_add_named(GTK_STACK(stack), createStep2(), "step2");
        gtk_stack_add_named(GTK_STACK(stack), createStep3(), "step3");

        gtk_box_pack_start(GTK_BOX(vbox), stack, TRUE, TRUE, 0);

        statusBar = gtk_statusbar_new();
        gtk_box_pack_start(GTK_BOX(vbox), statusBar, FALSE, FALSE, 0);

        updateStatusBar("Step 1: Select Backup & Date");
        gtk_widget_show_all(window);
    }

    void run() { gtk_main(); }
};

int main(int argc, char *argv[]) {
    gtk_init(&argc, &argv);
    RestoreGUI gui;
    gui.run();
    return 0;
}


#include <gtk/gtk.h>
#include <string>
#include <vector>
#include <functional>
#include "restore_engine.cpp"

class RestoreGUI {
private:
    GtkWidget *window;
    GtkWidget *stack;  // For wizard steps
    GtkWidget *statusBar;
    
    // Step 1 widgets
    GtkWidget *txtBackupPath;
    GtkWidget *btnBrowseBackup;
    GtkWidget *btnLoadDates;
    GtkWidget *treeViewDates;
    
    // Step 2 widgets
    GtkWidget *treeViewItems;
    GtkWidget *btnExpandAll;
    GtkWidget *btnCollapseAll;
    
    // Step 3 widgets
    GtkWidget *radioOriginal;
    GtkWidget *radioNew;
    GtkWidget *radioDiskTarget;   // new: restore to disk/partition
    GtkWidget *radioDisk;         // existing: metadata-driven disk reconstruction
    GtkWidget *txtDestPath;
    GtkWidget *btnBrowseDest;
    GtkWidget *chkOverwrite;
    GtkWidget *chkPreservePerms;
    GtkWidget *treeViewTargetDisks;  // new: disk target tree
    GtkWidget *lblDiskMapping;
    GtkWidget *boxDiskTarget;        // container shown when radioDiskTarget is selected
    GtkWidget *lblSelectedTarget;    // shows the currently selected target device
    
    // Progress dialog
    GtkWidget *progressDialog;
    GtkWidget *progressBar;
    GtkWidget *lblProgress;
    
    RestoreEngine engine;
    
    std::string selectedBackupPath;
    std::string selectedTargetDevice;   // device chosen in the disk target tree
    std::vector<RestoreEngine::BackupDate> backupDates;
    std::vector<RestoreEngine::RestoreItem> restoreTree;
    
    int currentStep = 0; // 0=Step1, 1=Step2, 2=Step3

    void ensurePasswordForSelectedBackup() {
        std::ifstream file(selectedBackupPath, std::ios::binary);
        if (!file) {
            return;
        }

        char header[7] = { 0 };
        file.read(header, sizeof(header));
        if (file.gcount() == sizeof(header) && std::strncmp(header, "SSBAES1", sizeof(header)) == 0) {
            GtkWidget* dialog = gtk_dialog_new_with_buttons(
                "Encrypted Backup Password",
                GTK_WINDOW(window),
                GTK_DIALOG_MODAL,
                "_Cancel", GTK_RESPONSE_CANCEL,
                "_OK", GTK_RESPONSE_OK,
                NULL);

            GtkWidget* content = gtk_dialog_get_content_area(GTK_DIALOG(dialog));
            GtkWidget* label = gtk_label_new("Enter the password for this encrypted backup:");
            GtkWidget* entry = gtk_entry_new();
            gtk_entry_set_visibility(GTK_ENTRY(entry), FALSE);
            gtk_box_pack_start(GTK_BOX(content), label, FALSE, FALSE, 5);
            gtk_box_pack_start(GTK_BOX(content), entry, FALSE, FALSE, 5);
            gtk_widget_show_all(dialog);

            if (gtk_dialog_run(GTK_DIALOG(dialog)) == GTK_RESPONSE_OK) {
                const char* password = gtk_entry_get_text(GTK_ENTRY(entry));
                engine.SetBackupPassword(password ? password : "");
            }

            gtk_widget_destroy(dialog);
        }
    }

    // Callbacks
    static void onBrowseBackup(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        
        GtkWidget *dialog = gtk_file_chooser_dialog_new(
            "Select Backup Folder",
            GTK_WINDOW(gui->window),
            GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER,
            "_Cancel", GTK_RESPONSE_CANCEL,
            "_Open", GTK_RESPONSE_ACCEPT,
            NULL);

        if (gtk_dialog_run(GTK_DIALOG(dialog)) == GTK_RESPONSE_ACCEPT) {
            char *filename = gtk_file_chooser_get_filename(GTK_FILE_CHOOSER(dialog));
            gtk_entry_set_text(GTK_ENTRY(gui->txtBackupPath), filename);
            g_free(filename);
        }

        gtk_widget_destroy(dialog);
    }

    static void onLoadDates(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        
        const char* path = gtk_entry_get_text(GTK_ENTRY(gui->txtBackupPath));
        if (strlen(path) == 0) {
            gui->showError("Please select a backup folder");
            return;
        }

        gui->loadBackupDates(path);
    }

    static void onStep1Next(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        
        GtkTreeSelection* selection = gtk_tree_view_get_selection(
            GTK_TREE_VIEW(gui->treeViewDates));
        
        if (gtk_tree_selection_count_selected_rows(selection) == 0) {
            gui->showError("Please select a backup date");
            return;
        }

        GtkTreeIter iter;
        GtkTreeModel* model;
        gtk_tree_selection_get_selected(selection, &model, &iter);
        
        gchar *path;
        gtk_tree_model_get(model, &iter, 3, &path, -1);
        gui->selectedBackupPath = path;
        g_free(path);

        gui->ensurePasswordForSelectedBackup();

        gui->loadBackupContents();
        gtk_stack_set_visible_child_name(GTK_STACK(gui->stack), "step2");
        gui->currentStep = 1;
        gui->updateStatusBar("Step 2: Select Items to Restore");
    }

    static void onStep2Back(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gtk_stack_set_visible_child_name(GTK_STACK(gui->stack), "step1");
        gui->currentStep = 0;
        gui->updateStatusBar("Step 1: Select Backup & Date");
    }

    static void onStep2Next(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);

        if (!gui->hasCheckedItems()) {
            gui->showError("Please select at least one item to restore");
            return;
        }

        // Pre-load the target disk tree so it is populated when the user arrives at Step 3.
        gui->loadTargetDisks();

        gtk_stack_set_visible_child_name(GTK_STACK(gui->stack), "step3");
        gui->currentStep = 2;
        gui->updateStatusBar("Step 3: Select Restore Destination");
    }

    static void onStep3Back(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gtk_stack_set_visible_child_name(GTK_STACK(gui->stack), "step2");
        gui->currentStep = 1;
        gui->updateStatusBar("Step 2: Select Items to Restore");
    }

    static void onBrowseDest(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        
        GtkWidget *dialog = gtk_file_chooser_dialog_new(
            "Select Restore Destination",
            GTK_WINDOW(gui->window),
            GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER,
            "_Cancel", GTK_RESPONSE_CANCEL,
            "_Select", GTK_RESPONSE_ACCEPT,
            NULL);

        if (gtk_dialog_run(GTK_DIALOG(dialog)) == GTK_RESPONSE_ACCEPT) {
            char *filename = gtk_file_chooser_get_filename(GTK_FILE_CHOOSER(dialog));
            gtk_entry_set_text(GTK_ENTRY(gui->txtDestPath), filename);
            g_free(filename);
        }

        gtk_widget_destroy(dialog);
    }

    static void onStartRestore(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gui->performRestore();
    }

    static void onExpandAll(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gtk_tree_view_expand_all(GTK_TREE_VIEW(gui->treeViewItems));
    }

    static void onCollapseAll(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gtk_tree_view_collapse_all(GTK_TREE_VIEW(gui->treeViewItems));
    }

    static void onCellToggled(GtkCellRendererToggle *renderer, gchar *path_str, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        
        GtkTreeModel *model = gtk_tree_view_get_model(GTK_TREE_VIEW(gui->treeViewItems));
        GtkTreePath *path = gtk_tree_path_new_from_string(path_str);
        GtkTreeIter iter;
        
        gtk_tree_model_get_iter(model, &iter, path);
        
        gboolean checked;
        gtk_tree_model_get(model, &iter, 0, &checked, -1);
        
        gtk_tree_store_set(GTK_TREE_STORE(model), &iter, 0, !checked, -1);
        
        gtk_tree_path_free(path);
    }

    static void onDestLocationChanged(GtkToggleButton *button, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);

        // Only update the active-radio's toggle; ignore deactivating partner radio signals.
        if (!gtk_toggle_button_get_active(button)) return;

        gboolean newLocation     = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(gui->radioNew));
        gboolean diskReconstruct = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(gui->radioDisk));

        // Alternate-folder path row is sensitive only in radioNew mode.
        gtk_widget_set_sensitive(gui->txtDestPath,   newLocation);
        gtk_widget_set_sensitive(gui->btnBrowseDest, newLocation);

        // Disk reconstruction guidance appears only for the metadata-driven mode.
        gtk_widget_set_visible(gui->lblDiskMapping, diskReconstruct);
    }

    static gboolean onDelete(GtkWidget *widget, GdkEvent *event, gpointer data) {
        gtk_main_quit();
        return FALSE;
    }

    // Callback: user clicked a row in the disk target tree
    static void onTargetDiskSelected(GtkTreeSelection *selection, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);

        GtkTreeIter iter;
        GtkTreeModel* model;
        if (!gtk_tree_selection_get_selected(selection, &model, &iter)) return;

        gboolean isBootDisk;
        gchar* device;
        gtk_tree_model_get(model, &iter, 0, &device, 2, &isBootDisk, -1);

        if (isBootDisk) {
            gui->showError("The boot/system disk cannot be used as a restore target. Please select a different disk or partition.");
            gtk_tree_selection_unselect_all(selection);
            g_free(device);
            return;
        }

        // Re-read the actual device path from column 3
        g_free(device);
        gchar* devicePath;
        gtk_tree_model_get(model, &iter, 3, &devicePath, -1);
        gui->selectedTargetDevice = devicePath ? devicePath : "";
        g_free(devicePath);

        std::string msg = "Selected target: " + gui->selectedTargetDevice;
        gtk_label_set_text(GTK_LABEL(gui->lblSelectedTarget), msg.c_str());

        // Auto-switch to "Restore to target disk (overwrite)" mode so the destination
        // is wired up, mirroring the Windows restore-target-tree behavior.
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(gui->radioDiskTarget), TRUE);
    }

    void loadTargetDisks() {
        auto disks = engine.ListTargetDisks();

        GtkTreeStore* store = GTK_TREE_STORE(
            gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewTargetDisks)));
        gtk_tree_store_clear(store);
        selectedTargetDevice.clear();
        gtk_label_set_text(GTK_LABEL(lblSelectedTarget), "No target selected");

        for (const auto& d : disks) {
            GtkTreeIter diskIter;
            gtk_tree_store_append(store, &diskIter, NULL);

            std::string diskLabel = "/dev/" + d.device + "  [disk]  " + d.size;
            if (d.isBootDisk) diskLabel += "  [BOOT - cannot restore]";

            gtk_tree_store_set(store, &diskIter,
                0, diskLabel.c_str(),            // display name
                1, d.size.c_str(),               // size
                2, (gboolean)d.isBootDisk,       // is boot disk
                3, ("/dev/" + d.device).c_str(), // device path
                -1);

            for (const auto& p : d.partitions) {
                GtkTreeIter partIter;
                gtk_tree_store_append(store, &partIter, &diskIter);

                std::string partLabel = "  /dev/" + p.device + "  " + p.size;
                if (!p.fsType.empty())     partLabel += "  " + p.fsType;
                if (!p.mountPoint.empty()) partLabel += "  " + p.mountPoint;
                if (d.isBootDisk)          partLabel += "  [boot]";

                gtk_tree_store_set(store, &partIter,
                    0, partLabel.c_str(),
                    1, p.size.c_str(),
                    2, (gboolean)d.isBootDisk,
                    3, ("/dev/" + p.device).c_str(),
                    -1);
            }
        }

        gtk_tree_view_expand_all(GTK_TREE_VIEW(treeViewTargetDisks));
        updateStatusBar("Target disks loaded");
    }

    void loadBackupDates(const std::string& backupPath) {
        backupDates = engine.EnumerateBackupDates(backupPath);
        
        GtkTreeStore* store = GTK_TREE_STORE(
            gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewDates)));
        gtk_tree_store_clear(store);
        
        if (backupDates.empty()) {
            showError("No backups found in the selected folder");
            return;
        }

        for (const auto& date : backupDates) {
            GtkTreeIter iter;
            gtk_tree_store_append(store, &iter, NULL);
            gtk_tree_store_set(store, &iter,
                0, date.date.c_str(),
                1, date.type.c_str(),
                2, date.size.c_str(),
                3, date.path.c_str(),
                -1);
        }

        updateStatusBar("Found " + std::to_string(backupDates.size()) + " backup(s)");
    }

    void loadBackupContents() {
        restoreTree = engine.BuildRestoreTree(selectedBackupPath);
        
        GtkTreeStore* store = GTK_TREE_STORE(
            gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewItems)));
        gtk_tree_store_clear(store);
        
        if (restoreTree.empty()) {
            showError("Failed to load backup contents");
            return;
        }

        for (const auto& item : restoreTree) {
            addTreeItem(store, NULL, item);
        }

        updateStatusBar("Backup contents loaded");
    }

    void addTreeItem(GtkTreeStore* store, GtkTreeIter* parent, 
                     const RestoreEngine::RestoreItem& item) {
        GtkTreeIter iter;
        gtk_tree_store_append(store, &iter, parent);
        gtk_tree_store_set(store, &iter,
            0, FALSE,  // checkbox unchecked
            1, item.name.c_str(),
            2, item.type.c_str(),
            3, item.path.c_str(),
            -1);
        
        // Add children recursively
        for (const auto& child : item.children) {
            addTreeItem(store, &iter, child);
        }
    }

    bool hasCheckedItems() {
        GtkTreeModel* model = gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewItems));
        return hasCheckedItemsRecursive(model, NULL);
    }

    bool hasCheckedItemsRecursive(GtkTreeModel* model, GtkTreeIter* parent) {
        GtkTreeIter iter;
        gboolean valid;
        
        if (parent == NULL) {
            valid = gtk_tree_model_get_iter_first(model, &iter);
        } else {
            valid = gtk_tree_model_iter_children(model, &iter, parent);
        }

        while (valid) {
            gboolean checked;
            gtk_tree_model_get(model, &iter, 0, &checked, -1);
            
            if (checked) return true;
            
            if (hasCheckedItemsRecursive(model, &iter)) return true;
            
            valid = gtk_tree_model_iter_next(model, &iter);
        }

        return false;
    }

    void collectCheckedItems(std::vector<std::string>& items) {
        GtkTreeModel* model = gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewItems));
        collectCheckedItemsRecursive(model, NULL, items);
    }

    void collectCheckedItemsRecursive(GtkTreeModel* model, GtkTreeIter* parent, 
                                      std::vector<std::string>& items) {
        GtkTreeIter iter;
        gboolean valid;
        
        if (parent == NULL) {
            valid = gtk_tree_model_get_iter_first(model, &iter);
        } else {
            valid = gtk_tree_model_iter_children(model, &iter, parent);
        }

        while (valid) {
            gboolean checked;
            gchar* path;
            
            gtk_tree_model_get(model, &iter, 0, &checked, 3, &path, -1);
            
            if (checked) {
                items.push_back(path);
            } else {
                collectCheckedItemsRecursive(model, &iter, items);
            }
            
            g_free(path);
            valid = gtk_tree_model_iter_next(model, &iter);
        }
    }

    void performRestore() {
        // Validate
        bool newLocation    = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(radioNew));
        bool diskTarget     = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(radioDiskTarget));
        std::string dest;

        if (newLocation) {
            dest = gtk_entry_get_text(GTK_ENTRY(txtDestPath));
            if (dest.empty()) {
                showError("Please select a restore destination");
                return;
            }
        } else if (diskTarget) {
            if (selectedTargetDevice.empty()) {
                showError("Please select a target disk or partition from the list");
                return;
            }
            dest = selectedTargetDevice;
        }

        // Collect checked items
        std::vector<std::string> items;
        collectCheckedItems(items);
        
        if (items.empty()) {
            showError("No items selected for restore");
            return;
        }

        std::string logPath;
        if (gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(chkSaveLog))) {
            logPath = gtk_entry_get_text(GTK_ENTRY(txtLogPath));
            if (logPath.empty()) {
                showError("Please choose a log file path or clear Save restore log");
                return;
            }
        }

        // Confirm
        GtkWidget *dialog = gtk_message_dialog_new(
            GTK_WINDOW(window),
            GTK_DIALOG_MODAL,
            GTK_MESSAGE_QUESTION,
            GTK_BUTTONS_YES_NO,
            "Are you sure you want to restore %d item(s)?",
            (int)items.size());

        int response = gtk_dialog_run(GTK_DIALOG(dialog));
        gtk_widget_destroy(dialog);

        if (response != GTK_RESPONSE_YES) {
            return;
        }

        // Show progress dialog
        showProgressDialog();

        engine.SetLogOperationName("Linux Restore");
        engine.SetLogFilePath(logPath);

        // Perform restore
        bool overwrite = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(chkOverwrite));
        
        auto callback = [this](int percent, const std::string& msg) {
            gtk_progress_bar_set_fraction(
                GTK_PROGRESS_BAR(progressBar), percent / 100.0);
            if (msg.rfind("Restoring:", 0) == 0 || msg.rfind("Processing:", 0) == 0) {
                gtk_label_set_text(GTK_LABEL(lblCurrentItem), msg.c_str());
            } else {
                gtk_label_set_text(GTK_LABEL(lblProgress), msg.c_str());
                if (msg.rfind("Restore", 0) != 0) {
                    gtk_label_set_text(GTK_LABEL(lblCurrentItem), "");
                }
            }
            
            // Process GTK events
            while (gtk_events_pending()) {
                gtk_main_iteration();
            }
        };

        bool success = engine.RestoreWithManifest(
            selectedBackupPath, dest, items, overwrite, callback);

        gtk_widget_destroy(progressDialog);

        if (success) {
            showInfo("Restore completed successfully!");
        } else {
            showError("Restore failed: " + engine.GetLastError());
        }
    }

    void showProgressDialog() {
        progressDialog = gtk_window_new(GTK_WINDOW_TOPLEVEL);
        gtk_window_set_title(GTK_WINDOW(progressDialog), "Restoring...");
        gtk_window_set_modal(GTK_WINDOW(progressDialog), TRUE);
        gtk_window_set_transient_for(GTK_WINDOW(progressDialog), GTK_WINDOW(window));
        gtk_window_set_default_size(GTK_WINDOW(progressDialog), 520, 140);

        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
        gtk_container_set_border_width(GTK_CONTAINER(vbox), 20);
        gtk_container_add(GTK_CONTAINER(progressDialog), vbox);

        lblProgress = gtk_label_new("Starting restore...");
        gtk_label_set_xalign(GTK_LABEL(lblProgress), 0.0f);
        gtk_box_pack_start(GTK_BOX(vbox), lblProgress, FALSE, FALSE, 0);

        lblCurrentItem = gtk_label_new("");
        gtk_label_set_xalign(GTK_LABEL(lblCurrentItem), 0.0f);
        gtk_label_set_line_wrap(GTK_LABEL(lblCurrentItem), TRUE);
        gtk_box_pack_start(GTK_BOX(vbox), lblCurrentItem, FALSE, FALSE, 0);

        progressBar = gtk_progress_bar_new();
        gtk_box_pack_start(GTK_BOX(vbox), progressBar, FALSE, FALSE, 0);

        gtk_widget_show_all(progressDialog);
    }

    void showError(const std::string& message) {
        GtkWidget *dialog = gtk_message_dialog_new(
            GTK_WINDOW(window),
            GTK_DIALOG_MODAL,
            GTK_MESSAGE_ERROR,
            GTK_BUTTONS_OK,
            "%s", message.c_str());
        
        gtk_dialog_run(GTK_DIALOG(dialog));
        gtk_widget_destroy(dialog);
    }

    void showInfo(const std::string& message) {
        GtkWidget *dialog = gtk_message_dialog_new(
            GTK_WINDOW(window),
            GTK_DIALOG_MODAL,
            GTK_MESSAGE_INFO,
            GTK_BUTTONS_OK,
            "%s", message.c_str());
        
        gtk_dialog_run(GTK_DIALOG(dialog));
        gtk_widget_destroy(dialog);
    }

    void updateStatusBar(const std::string& message) {
        gtk_statusbar_push(GTK_STATUSBAR(statusBar), 0, message.c_str());
    }

    GtkWidget* createStep1() {
        GtkWidget *box = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
        gtk_container_set_border_width(GTK_CONTAINER(box), 20);

        // Title
        GtkWidget *lblTitle = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblTitle), 
            "<b>Step 1: Select Backup and Date</b>");
        gtk_box_pack_start(GTK_BOX(box), lblTitle, FALSE, FALSE, 10);

        // Backup path
        GtkWidget *hbox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        GtkWidget *lblPath = gtk_label_new("Backup Folder:");
        gtk_box_pack_start(GTK_BOX(hbox), lblPath, FALSE, FALSE, 0);
        
        txtBackupPath = gtk_entry_new();
        gtk_box_pack_start(GTK_BOX(hbox), txtBackupPath, TRUE, TRUE, 0);
        
        btnBrowseBackup = gtk_button_new_with_label("Browse...");
        g_signal_connect(btnBrowseBackup, "clicked", G_CALLBACK(onBrowseBackup), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnBrowseBackup, FALSE, FALSE, 0);
        
        gtk_box_pack_start(GTK_BOX(box), hbox, FALSE, FALSE, 0);

        // Load button
        btnLoadDates = gtk_button_new_with_label("Load Backup Dates");
        g_signal_connect(btnLoadDates, "clicked", G_CALLBACK(onLoadDates), this);
        gtk_box_pack_start(GTK_BOX(box), btnLoadDates, FALSE, FALSE, 0);

        // Dates list
        GtkWidget *scrolled = gtk_scrolled_window_new(NULL, NULL);
        gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scrolled),
            GTK_POLICY_AUTOMATIC, GTK_POLICY_AUTOMATIC);
        gtk_widget_set_vexpand(scrolled, TRUE);

        GtkTreeStore *store = gtk_tree_store_new(4, G_TYPE_STRING, G_TYPE_STRING, 
                                                  G_TYPE_STRING, G_TYPE_STRING);
        treeViewDates = gtk_tree_view_new_with_model(GTK_TREE_MODEL(store));
        
        GtkCellRenderer *renderer = gtk_cell_renderer_text_new();
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewDates),
            -1, "Date", renderer, "text", 0, NULL);
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewDates),
            -1, "Type", renderer, "text", 1, NULL);
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewDates),
            -1, "Size", renderer, "text", 2, NULL);

        gtk_container_add(GTK_CONTAINER(scrolled), treeViewDates);
        gtk_box_pack_start(GTK_BOX(box), scrolled, TRUE, TRUE, 0);

        // Next button
        GtkWidget *btnNext = gtk_button_new_with_label("Next >");
        g_signal_connect(btnNext, "clicked", G_CALLBACK(onStep1Next), this);
        gtk_box_pack_start(GTK_BOX(box), btnNext, FALSE, FALSE, 0);

        return box;
    }

    GtkWidget* createStep2() {
        GtkWidget *box = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
        gtk_container_set_border_width(GTK_CONTAINER(box), 20);

        // Title
        GtkWidget *lblTitle = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblTitle), 
            "<b>Step 2: Select Items to Restore</b>");
        gtk_box_pack_start(GTK_BOX(box), lblTitle, FALSE, FALSE, 10);

        // Tree view
        GtkWidget *scrolled = gtk_scrolled_window_new(NULL, NULL);
        gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scrolled),
            GTK_POLICY_AUTOMATIC, GTK_POLICY_AUTOMATIC);
        gtk_widget_set_vexpand(scrolled, TRUE);

        GtkTreeStore *store = gtk_tree_store_new(4, G_TYPE_BOOLEAN, G_TYPE_STRING, 
                                                  G_TYPE_STRING, G_TYPE_STRING);
        treeViewItems = gtk_tree_view_new_with_model(GTK_TREE_MODEL(store));

        // Checkbox column
        GtkCellRenderer *toggleRenderer = gtk_cell_renderer_toggle_new();
        g_signal_connect(toggleRenderer, "toggled", G_CALLBACK(onCellToggled), this);
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewItems),
            -1, "", toggleRenderer, "active", 0, NULL);

        // Name column
        GtkCellRenderer *textRenderer = gtk_cell_renderer_text_new();
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewItems),
            -1, "Name", textRenderer, "text", 1, NULL);

        // Type column
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewItems),
            -1, "Type", textRenderer, "text", 2, NULL);

        gtk_container_add(GTK_CONTAINER(scrolled), treeViewItems);
        gtk_box_pack_start(GTK_BOX(box), scrolled, TRUE, TRUE, 0);

        // Buttons
        GtkWidget *hbox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        
        btnExpandAll = gtk_button_new_with_label("Expand All");
        g_signal_connect(btnExpandAll, "clicked", G_CALLBACK(onExpandAll), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnExpandAll, FALSE, FALSE, 0);

        btnCollapseAll = gtk_button_new_with_label("Collapse All");
        g_signal_connect(btnCollapseAll, "clicked", G_CALLBACK(onCollapseAll), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnCollapseAll, FALSE, FALSE, 0);

        gtk_box_pack_start(GTK_BOX(box), hbox, FALSE, FALSE, 0);

        // Navigation buttons
        GtkWidget *navBox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        
        GtkWidget *btnBack = gtk_button_new_with_label("< Back");
        g_signal_connect(btnBack, "clicked", G_CALLBACK(onStep2Back), this);
        gtk_box_pack_start(GTK_BOX(navBox), btnBack, FALSE, FALSE, 0);

        GtkWidget *btnNext = gtk_button_new_with_label("Next >");
        g_signal_connect(btnNext, "clicked", G_CALLBACK(onStep2Next), this);
        gtk_box_pack_end(GTK_BOX(navBox), btnNext, FALSE, FALSE, 0);

        gtk_box_pack_start(GTK_BOX(box), navBox, FALSE, FALSE, 0);

        return box;
    }

    GtkWidget* createStep3() {
        GtkWidget *box = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
        gtk_container_set_border_width(GTK_CONTAINER(box), 20);

        // Title
        GtkWidget *lblTitle = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblTitle),
            "<b>Step 3: Select Restore Destination</b>");
        gtk_box_pack_start(GTK_BOX(box), lblTitle, FALSE, FALSE, 10);

        // ------------------------------------------------------------------
        // Restore target disk/partition tree — always visible, mirrors the
        // Windows restore page. Boot disk is shown greyed and unselectable.
        // ------------------------------------------------------------------
        GtkWidget *lblTargetHdr = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblTargetHdr),
            "<b>Select Restore Target Disk or Partition:</b>\n"
            "<small>Click any non-boot disk or partition below. "
            "Boot/system disk is shown greyed and cannot be selected.</small>");
        gtk_label_set_xalign(GTK_LABEL(lblTargetHdr), 0.0f);
        gtk_box_pack_start(GTK_BOX(box), lblTargetHdr, FALSE, FALSE, 0);

        boxDiskTarget = gtk_box_new(GTK_ORIENTATION_VERTICAL, 4);

        GtkWidget *scrolledDisks = gtk_scrolled_window_new(NULL, NULL);
        gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scrolledDisks),
            GTK_POLICY_AUTOMATIC, GTK_POLICY_AUTOMATIC);
        gtk_widget_set_size_request(scrolledDisks, -1, 200);
        gtk_widget_set_vexpand(scrolledDisks, TRUE);

        // Store: display name, size, isBootDisk (gboolean), device path
        GtkTreeStore *diskStore = gtk_tree_store_new(4,
            G_TYPE_STRING, G_TYPE_STRING, G_TYPE_BOOLEAN, G_TYPE_STRING);
        treeViewTargetDisks = gtk_tree_view_new_with_model(GTK_TREE_MODEL(diskStore));

        GtkCellRenderer *textRend = gtk_cell_renderer_text_new();
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewTargetDisks),
            -1, "Device", textRend, "text", 0, NULL);
        gtk_tree_view_insert_column_with_attributes(GTK_TREE_VIEW(treeViewTargetDisks),
            -1, "Size", textRend, "text", 1, NULL);

        GtkTreeSelection *diskSel = gtk_tree_view_get_selection(GTK_TREE_VIEW(treeViewTargetDisks));
        gtk_tree_selection_set_mode(diskSel, GTK_SELECTION_SINGLE);
        g_signal_connect(diskSel, "changed", G_CALLBACK(onTargetDiskSelected), this);

        gtk_container_add(GTK_CONTAINER(scrolledDisks), treeViewTargetDisks);
        gtk_box_pack_start(GTK_BOX(boxDiskTarget), scrolledDisks, TRUE, TRUE, 0);

        lblSelectedTarget = gtk_label_new("No target selected");
        gtk_label_set_xalign(GTK_LABEL(lblSelectedTarget), 0.0f);
        gtk_box_pack_start(GTK_BOX(boxDiskTarget), lblSelectedTarget, FALSE, FALSE, 0);

        gtk_box_pack_start(GTK_BOX(box), boxDiskTarget, TRUE, TRUE, 5);

        // ------------------------------------------------------------------
        // Destination mode radios — used to fine-tune where the restore lands
        // once a target disk/partition has been chosen above.
        // ------------------------------------------------------------------
        GtkWidget *lblDestMode = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(lblDestMode), "<b>Restore Mode:</b>");
        gtk_label_set_xalign(GTK_LABEL(lblDestMode), 0.0f);
        gtk_box_pack_start(GTK_BOX(box), lblDestMode, FALSE, FALSE, 4);

        radioOriginal = gtk_radio_button_new_with_label(NULL,
            "Restore to original location");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(radioOriginal), TRUE);
        g_signal_connect(radioOriginal, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(box), radioOriginal, FALSE, FALSE, 0);

        radioNew = gtk_radio_button_new_with_label_from_widget(
            GTK_RADIO_BUTTON(radioOriginal), "Restore to alternate folder / selected target");
        g_signal_connect(radioNew, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(box), radioNew, FALSE, FALSE, 0);

        radioDiskTarget = gtk_radio_button_new_with_label_from_widget(
            GTK_RADIO_BUTTON(radioOriginal), "Restore to target disk (overwrite)");
        g_signal_connect(radioDiskTarget, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(box), radioDiskTarget, FALSE, FALSE, 0);

        radioDisk = gtk_radio_button_new_with_label_from_widget(
            GTK_RADIO_BUTTON(radioOriginal), "Metadata-driven disk reconstruction restore");
        g_signal_connect(radioDisk, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(box), radioDisk, FALSE, FALSE, 0);

        // Alternate folder path entry (shown when radioNew is active)
        GtkWidget *hbox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        GtkWidget *lblDest = gtk_label_new("Alternate folder:");
        gtk_box_pack_start(GTK_BOX(hbox), lblDest, FALSE, FALSE, 0);

        txtDestPath = gtk_entry_new();
        gtk_widget_set_sensitive(txtDestPath, FALSE);
        gtk_box_pack_start(GTK_BOX(hbox), txtDestPath, TRUE, TRUE, 0);

        btnBrowseDest = gtk_button_new_with_label("Browse...");
        gtk_widget_set_sensitive(btnBrowseDest, FALSE);
        g_signal_connect(btnBrowseDest, "clicked", G_CALLBACK(onBrowseDest), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnBrowseDest, FALSE, FALSE, 0);

        gtk_box_pack_start(GTK_BOX(box), hbox, FALSE, FALSE, 5);

        chkSaveLog = gtk_check_button_new_with_label("Save restore log to a text file");
        gtk_box_pack_start(GTK_BOX(box), chkSaveLog, FALSE, FALSE, 0);

        GtkWidget *logBox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        GtkWidget *lblLog = gtk_label_new("Log file:");
        gtk_box_pack_start(GTK_BOX(logBox), lblLog, FALSE, FALSE, 0);

        txtLogPath = gtk_entry_new();
        gtk_entry_set_placeholder_text(GTK_ENTRY(txtLogPath), "/path/to/restore-log.txt");
        gtk_box_pack_start(GTK_BOX(logBox), txtLogPath, TRUE, TRUE, 0);

        btnBrowseLog = gtk_button_new_with_label("Browse Log...");
        g_signal_connect(btnBrowseLog, "clicked", G_CALLBACK(onBrowseLog), this);
        gtk_box_pack_start(GTK_BOX(logBox), btnBrowseLog, FALSE, FALSE, 0);

        gtk_box_pack_start(GTK_BOX(box), logBox, FALSE, FALSE, 5);

        // Disk reconstruction guidance
        lblDiskMapping = gtk_label_new(
            "For disk reconstruction restore, the restore engine rebuilds the partition\n"
            "layout from backup metadata onto the selected target disk.");
        gtk_label_set_line_wrap(GTK_LABEL(lblDiskMapping), TRUE);
        gtk_label_set_xalign(GTK_LABEL(lblDiskMapping), 0.0f);
        gtk_widget_set_visible(lblDiskMapping, FALSE);
        gtk_box_pack_start(GTK_BOX(box), lblDiskMapping, FALSE, FALSE, 4);

        // Options
        chkOverwrite = gtk_check_button_new_with_label("Overwrite existing files");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(chkOverwrite), TRUE);
        gtk_box_pack_start(GTK_BOX(box), chkOverwrite, FALSE, FALSE, 0);

        chkPreservePerms = gtk_check_button_new_with_label(
            "Preserve file permissions and ownership");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(chkPreservePerms), TRUE);
        gtk_box_pack_start(GTK_BOX(box), chkPreservePerms, FALSE, FALSE, 0);

        // Spacer
        GtkWidget *spacer = gtk_box_new(GTK_ORIENTATION_VERTICAL, 0);
        gtk_widget_set_vexpand(spacer, TRUE);
        gtk_box_pack_start(GTK_BOX(box), spacer, TRUE, TRUE, 0);

        // Navigation buttons
        GtkWidget *navBox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);

        GtkWidget *btnBack = gtk_button_new_with_label("< Back");
        g_signal_connect(btnBack, "clicked", G_CALLBACK(onStep3Back), this);
        gtk_box_pack_start(GTK_BOX(navBox), btnBack, FALSE, FALSE, 0);

        GtkWidget *btnRestore = gtk_button_new_with_label("Start Restore");
        g_signal_connect(btnRestore, "clicked", G_CALLBACK(onStartRestore), this);
        gtk_box_pack_end(GTK_BOX(navBox), btnRestore, FALSE, FALSE, 0);

        gtk_box_pack_start(GTK_BOX(box), navBox, FALSE, FALSE, 0);

        return box;
    }

public:
    RestoreGUI() {
        // Create window
        window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
        gtk_window_set_title(GTK_WINDOW(window), 
            "Backup & Restore - Linux Recovery v4.7.2");
        gtk_window_set_default_size(GTK_WINDOW(window), 800, 600);
        g_signal_connect(window, "delete-event", G_CALLBACK(onDelete), NULL);

        // Main layout
        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 0);
        gtk_container_add(GTK_CONTAINER(window), vbox);

        // Create stack for wizard steps
        stack = gtk_stack_new();
        gtk_stack_set_transition_type(GTK_STACK(stack), 
            GTK_STACK_TRANSITION_TYPE_SLIDE_LEFT_RIGHT);
        gtk_widget_set_vexpand(stack, TRUE);

        gtk_stack_add_named(GTK_STACK(stack), createStep1(), "step1");
        gtk_stack_add_named(GTK_STACK(stack), createStep2(), "step2");
        gtk_stack_add_named(GTK_STACK(stack), createStep3(), "step3");

        gtk_box_pack_start(GTK_BOX(vbox), stack, TRUE, TRUE, 0);

        // Status bar
        statusBar = gtk_statusbar_new();
        gtk_box_pack_start(GTK_BOX(vbox), statusBar, FALSE, FALSE, 0);

        updateStatusBar("Step 1: Select Backup & Date");

        gtk_widget_show_all(window);
    }

    void run() {
        gtk_main();
    }
};

int main(int argc, char *argv[]) {
    gtk_init(&argc, &argv);
    
    RestoreGUI gui;
    gui.run();
    
    return 0;
}
