// LinuxRestore/restore_gui_gtk.cpp
// Graphical User Interface using GTK+ 3.0 - Version 4.7.1.0
// Enhanced with 3-step wizard matching Windows restore interface

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
    GtkWidget *txtDestPath;
    GtkWidget *btnBrowseDest;
    GtkWidget *chkOverwrite;
    GtkWidget *chkPreservePerms;
    
    // Progress dialog
    GtkWidget *progressDialog;
    GtkWidget *progressBar;
    GtkWidget *lblProgress;
    
    RestoreEngine engine;
    
    std::string selectedBackupPath;
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
        
        gboolean newLocation = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(gui->radioNew));
        gtk_widget_set_sensitive(gui->txtDestPath, newLocation);
        gtk_widget_set_sensitive(gui->btnBrowseDest, newLocation);
    }

    static gboolean onDelete(GtkWidget *widget, GdkEvent *event, gpointer data) {
        gtk_main_quit();
        return FALSE;
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
        bool newLocation = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(radioNew));
        std::string dest;
        
        if (newLocation) {
            dest = gtk_entry_get_text(GTK_ENTRY(txtDestPath));
            if (dest.empty()) {
                showError("Please select a restore destination");
                return;
            }
        }

        // Collect checked items
        std::vector<std::string> items;
        collectCheckedItems(items);
        
        if (items.empty()) {
            showError("No items selected for restore");
            return;
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

        // Perform restore
        bool overwrite = gtk_toggle_button_get_active(GTK_TOGGLE_BUTTON(chkOverwrite));
        
        auto callback = [this](int percent, const std::string& msg) {
            gtk_progress_bar_set_fraction(
                GTK_PROGRESS_BAR(progressBar), percent / 100.0);
            gtk_label_set_text(GTK_LABEL(lblProgress), msg.c_str());
            
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
        gtk_window_set_default_size(GTK_WINDOW(progressDialog), 400, 100);

        GtkWidget *vbox = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
        gtk_container_set_border_width(GTK_CONTAINER(vbox), 20);
        gtk_container_add(GTK_CONTAINER(progressDialog), vbox);

        lblProgress = gtk_label_new("Starting restore...");
        gtk_box_pack_start(GTK_BOX(vbox), lblProgress, FALSE, FALSE, 0);

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

        // Destination options
        radioOriginal = gtk_radio_button_new_with_label(NULL, 
            "Restore to original location");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(radioOriginal), TRUE);
        g_signal_connect(radioOriginal, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(box), radioOriginal, FALSE, FALSE, 0);

        radioNew = gtk_radio_button_new_with_label_from_widget(
            GTK_RADIO_BUTTON(radioOriginal), "Restore to new location");
        g_signal_connect(radioNew, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(box), radioNew, FALSE, FALSE, 0);

        radioDisk = gtk_radio_button_new_with_label_from_widget(
            GTK_RADIO_BUTTON(radioOriginal), "Metadata-driven disk reconstruction restore");
        g_signal_connect(radioDisk, "toggled", G_CALLBACK(onDestLocationChanged), this);
        gtk_box_pack_start(GTK_BOX(box), radioDisk, FALSE, FALSE, 0);

        // Destination path
        GtkWidget *hbox = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 5);
        GtkWidget *lblDest = gtk_label_new("Destination:");
        gtk_box_pack_start(GTK_BOX(hbox), lblDest, FALSE, FALSE, 0);
        
        txtDestPath = gtk_entry_new();
        gtk_widget_set_sensitive(txtDestPath, FALSE);
        gtk_box_pack_start(GTK_BOX(hbox), txtDestPath, TRUE, TRUE, 0);
        
        btnBrowseDest = gtk_button_new_with_label("Browse...");
        gtk_widget_set_sensitive(btnBrowseDest, FALSE);
        g_signal_connect(btnBrowseDest, "clicked", G_CALLBACK(onBrowseDest), this);
        gtk_box_pack_start(GTK_BOX(hbox), btnBrowseDest, FALSE, FALSE, 0);
        
        gtk_box_pack_start(GTK_BOX(box), hbox, FALSE, FALSE, 10);

        // Options
        chkOverwrite = gtk_check_button_new_with_label("Overwrite existing files");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(chkOverwrite), TRUE);
        gtk_box_pack_start(GTK_BOX(box), chkOverwrite, FALSE, FALSE, 0);

        chkPreservePerms = gtk_check_button_new_with_label(
            "Preserve file permissions and ownership");
        gtk_toggle_button_set_active(GTK_TOGGLE_BUTTON(chkPreservePerms), TRUE);
        gtk_box_pack_start(GTK_BOX(box), chkPreservePerms, FALSE, FALSE, 0);

        // Disk mapping guidance
        lblDiskMapping = gtk_label_new("For disk reconstruction restore, select a target disk device (e.g. /dev/sda) and the restore engine will rebuild the partition layout from backup metadata.");
        gtk_label_set_line_wrap(GTK_LABEL(lblDiskMapping), TRUE);
        gtk_box_pack_start(GTK_BOX(box), lblDiskMapping, FALSE, FALSE, 8);

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
            "Backup & Restore - Linux Recovery v4.7.1");
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
