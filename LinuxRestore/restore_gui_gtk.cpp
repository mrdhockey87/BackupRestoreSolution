// LinuxRestore/restore_gui_gtk.cpp
// Full Graphical User Interface using GTK+

#include <gtk/gtk.h>
#include <string>
#include <vector>
#include "restore_engine.cpp"

class RestoreGUI {
private:
    GtkWidget *window;
    GtkWidget *notebook;
    GtkWidget *progressBar;
    GtkWidget *statusLabel;
    
    GtkWidget *diskList;
    GtkWidget *backupList;
    
    RestoreEngine engine;
    
    std::string selectedDisk;
    std::string selectedBackup;

    // Callbacks
    static void onScanDisks(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gui->scanDisks();
    }

    static void onScanBackups(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gui->scanBackups();
    }

    static void onRestore(GtkWidget *widget, gpointer data) {
        RestoreGUI* gui = static_cast<RestoreGUI*>(data);
        gui->performRestore();
    }

    static void onQuit(GtkWidget *widget, gpointer data) {
        gtk_main_quit();
    }

    void scanDisks() {
        auto disks = engine.ListDisks();
        
        GtkListStore *store = GTK_LIST_STORE(gtk_tree_view_get_model(GTK_TREE_VIEW(diskList)));
        gtk_list_store_clear(store);
        
        for (const auto& disk : disks) {
            GtkTreeIter iter;
            gtk_list_store_append(store, &iter);
            gtk_list_store_set(store, &iter, 0, disk.c_str(), -1);
        }
        
        gtk_label_set_text(GTK_LABEL(statusLabel), 
            ("Found " + std::to_string(disks.size()) + " disk(s)").c_str());
    }

    void scanBackups() {
        std::vector<std::string> searchPaths = {"/media", "/mnt", "/run/media"};
        std::vector<std::string> allBackups;
        
        for (const auto& path : searchPaths) {
            auto backups = engine.ScanForBackups(path);
            allBackups.insert(allBackups.end(), backups.begin(), backups.end());
        }
        
        GtkListStore *store = GTK_LIST_STORE(gtk_tree_view_get_model(GTK_TREE_VIEW(backupList)));
        gtk_list_store_clear(store);
        
        for (const auto& backup : allBackups) {
            GtkTreeIter iter;
            gtk_list_store_append(store, &iter);
            gtk_list_store_set(store, &iter, 0, backup.c_str(), -1);
        }
        
        gtk_label_set_text(GTK_LABEL(statusLabel), 
            ("Found " + std::to_string(allBackups.size()) + " backup(s)").c_str());
    }

    void performRestore() {
        // Get selected disk
        GtkTreeSelection *selection = gtk_tree_view_get_selection(GTK_TREE_VIEW(diskList));
        GtkTreeIter iter;
        GtkTreeModel *model;
        
        if (gtk_tree_selection_get_selected(selection, &model, &iter)) {
            gchar *disk;
            gtk_tree_model_get(model, &iter, 0, &disk, -1);
            selectedDisk = disk;
            g_free(disk);
        } else {
            showError("Please select a target disk");
            return;
        }

        // Get selected backup
        selection = gtk_tree_view_get_selection(GTK_TREE_VIEW(backupList));
        if (gtk_tree_selection_get_selected(selection, &model, &iter)) {
            gchar *backup;
            gtk_tree_model_get(model, &iter, 0, &backup, -1);
            selectedBackup = backup;
            g_free(backup);
        } else {
            showError("Please select a backup");
            return;
        }

        // Confirm
        GtkWidget *dialog = gtk_message_dialog_new(
            GTK_WINDOW(window),
            GTK_DIALOG_DESTROY_WITH_PARENT,
            GTK_MESSAGE_WARNING,
            GTK_BUTTONS_YES_NO,
            "Restore from:\n%s\n\nTo:\n%s\n\nWARNING: This will OVERWRITE data!\n\nContinue?",
            selectedBackup.c_str(),
            selectedDisk.c_str());

        gint response = gtk_dialog_run(GTK_DIALOG(dialog));
        gtk_widget_destroy(dialog);

        if (response != GTK_RESPONSE_YES) {
            return;
        }

        // Mount and restore
        gtk_label_set_text(GTK_LABEL(statusLabel), "Mounting partition...");
        gtk_progress_bar_set_fraction(GTK_PROGRESS_BAR(progressBar), 0.1);
        
        std::string mountPoint = "/mnt/restore";
        if (engine.MountNTFSPartition(selectedDisk, mountPoint) != 0) {
            showError("Failed to mount partition: " + engine.GetLastError());
            return;
        }

        // Restore
        gtk_label_set_text(GTK_LABEL(statusLabel), "Restoring files...");
        
        int result = engine.RestoreFiles(selectedBackup, mountPoint, true);

        engine.UnmountPartition(mountPoint);

        if (result == 0) {
            gtk_progress_bar_set_fraction(GTK_PROGRESS_BAR(progressBar), 1.0);
            showMessage("Restore completed successfully!");
        } else {
            showError("Restore failed: " + engine.GetLastError());
        }
    }

    void showError(const std::string& message) {
        GtkWidget *dialog = gtk_message_dialog_new(
            GTK_WINDOW(window),
            GTK_DIALOG_DESTROY_WITH_PARENT,
            GTK_MESSAGE_ERROR,
            GTK_BUTTONS_CLOSE,
            "%s", message.c_str());
        gtk_dialog_run(GTK_DIALOG(dialog));
        gtk_widget_destroy(dialog);
    }

    void showMessage(const std::string& message) {
        GtkWidget *dialog = gtk_message_dialog_new(
            GTK_WINDOW(window),
            GTK_DIALOG_DESTROY_WITH_PARENT,
            GTK_MESSAGE_INFO,
            GTK_BUTTONS_OK,
            "%s", message.c_str());
        gtk_dialog_run(GTK_DIALOG(dialog));
        gtk_widget_destroy(dialog);
    }

public:
    RestoreGUI() {
        // Create main window
        window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
        gtk_window_set_title(GTK_WINDOW(window), "Backup & Restore - Linux Recovery");
        gtk_window_set_default_size(GTK_WINDOW(window), 800, 600);
        gtk_container_set_border_width(GTK_CONTAINER(window), 10);
        
        g_signal_connect(window, "destroy", G_CALLBACK(onQuit), NULL);

        // Create main vbox
        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 5);
        gtk_container_add(GTK_CONTAINER(window), vbox);

        // Title
        GtkWidget *title = gtk_label_new(NULL);
        gtk_label_set_markup(GTK_LABEL(title), 
            "<span size='xx-large' weight='bold'>Backup &amp; Restore</span>\n"
            "<span size='large'>Linux Recovery Mode - Version 4.6.0</span>");
        gtk_box_pack_start(GTK_BOX(vbox), title, FALSE, FALSE, 10);

        // Notebook (tabs)
        notebook = gtk_notebook_new();
        gtk_box_pack_start(GTK_BOX(vbox), notebook, TRUE, TRUE, 0);

        // Tab 1: Select Disk
        GtkWidget *diskTab = createDiskTab();
        gtk_notebook_append_page(GTK_NOTEBOOK(notebook), diskTab, 
            gtk_label_new("1. Select Disk"));

        // Tab 2: Select Backup
        GtkWidget *backupTab = createBackupTab();
        gtk_notebook_append_page(GTK_NOTEBOOK(notebook), backupTab, 
            gtk_label_new("2. Select Backup"));

        // Tab 3: Restore
        GtkWidget *restoreTab = createRestoreTab();
        gtk_notebook_append_page(GTK_NOTEBOOK(notebook), restoreTab, 
            gtk_label_new("3. Restore"));

        // Status bar
        statusLabel = gtk_label_new("Ready");
        gtk_box_pack_start(GTK_BOX(vbox), statusLabel, FALSE, FALSE, 5);

        // Progress bar
        progressBar = gtk_progress_bar_new();
        gtk_box_pack_start(GTK_BOX(vbox), progressBar, FALSE, FALSE, 5);

        gtk_widget_show_all(window);
    }

    GtkWidget* createDiskTab() {
        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 5);
        gtk_container_set_border_width(GTK_CONTAINER(vbox), 10);

        GtkWidget *label = gtk_label_new("Select the target disk/partition to restore to:");
        gtk_box_pack_start(GTK_BOX(vbox), label, FALSE, FALSE, 5);

        // Disk list
        GtkListStore *store = gtk_list_store_new(1, G_TYPE_STRING);
        diskList = gtk_tree_view_new_with_model(GTK_TREE_MODEL(store));
        
        GtkCellRenderer *renderer = gtk_cell_renderer_text_new();
        GtkTreeViewColumn *column = gtk_tree_view_column_new_with_attributes(
            "Disk", renderer, "text", 0, NULL);
        gtk_tree_view_append_column(GTK_TREE_VIEW(diskList), column);

        GtkWidget *scrolled = gtk_scrolled_window_new(NULL, NULL);
        gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scrolled),
            GTK_POLICY_AUTOMATIC, GTK_POLICY_AUTOMATIC);
        gtk_container_add(GTK_CONTAINER(scrolled), diskList);
        gtk_box_pack_start(GTK_BOX(vbox), scrolled, TRUE, TRUE, 0);

        // Scan button
        GtkWidget *scanBtn = gtk_button_new_with_label("Scan for Disks");
        g_signal_connect(scanBtn, "clicked", G_CALLBACK(onScanDisks), this);
        gtk_box_pack_start(GTK_BOX(vbox), scanBtn, FALSE, FALSE, 5);

        return vbox;
    }

    GtkWidget* createBackupTab() {
        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 5);
        gtk_container_set_border_width(GTK_CONTAINER(vbox), 10);

        GtkWidget *label = gtk_label_new("Select the backup to restore:");
        gtk_box_pack_start(GTK_BOX(vbox), label, FALSE, FALSE, 5);

        // Backup list
        GtkListStore *store = gtk_list_store_new(1, G_TYPE_STRING);
        backupList = gtk_tree_view_new_with_model(GTK_TREE_MODEL(store));
        
        GtkCellRenderer *renderer = gtk_cell_renderer_text_new();
        GtkTreeViewColumn *column = gtk_tree_view_column_new_with_attributes(
            "Backup", renderer, "text", 0, NULL);
        gtk_tree_view_append_column(GTK_TREE_VIEW(backupList), column);

        GtkWidget *scrolled = gtk_scrolled_window_new(NULL, NULL);
        gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scrolled),
            GTK_POLICY_AUTOMATIC, GTK_POLICY_AUTOMATIC);
        gtk_container_add(GTK_CONTAINER(scrolled), backupList);
        gtk_box_pack_start(GTK_BOX(vbox), scrolled, TRUE, TRUE, 0);

        // Scan button
        GtkWidget *scanBtn = gtk_button_new_with_label("Scan for Backups");
        g_signal_connect(scanBtn, "clicked", G_CALLBACK(onScanBackups), this);
        gtk_box_pack_start(GTK_BOX(vbox), scanBtn, FALSE, FALSE, 5);

        return vbox;
    }

    GtkWidget* createRestoreTab() {
        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 5);
        gtk_container_set_border_width(GTK_CONTAINER(vbox), 10);

        GtkWidget *label = gtk_label_new(
            "Click 'Start Restore' to begin the restore process.\n\n"
            "WARNING: This will OVERWRITE data on the target disk!");
        gtk_box_pack_start(GTK_BOX(vbox), label, FALSE, FALSE, 20);

        // Restore button
        GtkWidget *restoreBtn = gtk_button_new_with_label("Start Restore");
        gtk_widget_set_size_request(restoreBtn, -1, 50);
        g_signal_connect(restoreBtn, "clicked", G_CALLBACK(onRestore), this);
        gtk_box_pack_start(GTK_BOX(vbox), restoreBtn, FALSE, FALSE, 10);

        return vbox;
    }

    void run() {
        gtk_main();
    }
};

int main(int argc, char *argv[]) {
    if (geteuid() != 0) {
        g_printerr("This program must be run as root (use sudo)\n");
        return 1;
    }

    gtk_init(&argc, &argv);
    
    RestoreGUI gui;
    gui.run();

    return 0;
}
