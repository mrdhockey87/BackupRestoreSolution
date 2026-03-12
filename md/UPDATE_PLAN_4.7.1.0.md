# Linux Restore Tools Update - Version 4.7.1.0

## Overview
This document outlines the updates needed for the Linux restore applications (restore_tui, restore_cli, restore_gui) to match the Windows restore workflow implemented in version 4.7.0.0.

## Required Changes for All Three Applications

### 1. **restore_tui.cpp** (ncurses Terminal UI)
**Status**: ? CREATED as `restore_tui_new.cpp`

**Features Implemented**:
- 3-step wizard interface matching Windows version
- Step 1: Browse for backup folder ? Load and display backup dates
- Step 2: Tree view with checkbox selection for drives/volumes/files/folders  
- Step 3: Destination selection (original or new location)
- Navigation: UP/DOWN arrows, SPACE to toggle, N for Next, B for Back
- Color-coded UI with status messages and progress bars

**Key Methods**:
- `LoadBackupDates()` - Calls engine->EnumerateBackupDates()
- `SelectBackupDate()` - Shows list of dates with type and size
- `LoadBackupContents()` - Calls engine->BuildRestoreTree()
- `SelectRestoreItems()` - Interactive tree with checkboxes
- `SelectDestination()` - Choose original or new location
- `PerformRestore()` - Calls engine->RestoreWithManifest()

---

### 2. **restore_cli.cpp** (Command-Line Interface)
**Status**: ?? NEEDS UPDATE

**Required Changes**:
```cpp
// New command-line arguments:
// restore_cli --list-dates /path/to/backup
// restore_cli --show-contents /path/to/backup/Full_20260130
// restore_cli --restore /path/to/backup/Full_20260130 --items "/dev/sda1,/home/user" --dest /mnt/restore

void showUsage() {
    std::cout << "Usage:\n";
    std::cout << "  restore_cli [options]\n\n";
    std::cout << "Options:\n";
    std::cout << "  --list-dates <path>           List available backup dates\n";
    std::cout << "  --show-contents <backup>      Show contents of specific backup\n";
    std::cout << "  --restore <backup>            Start restore from backup\n";
    std::cout << "    --items <paths>             Comma-separated list of items\n";
    std::cout << "    --dest <path>               Destination (omit for original)\n";
    std::cout << "    --interactive               Interactive mode with menus\n";
}

void listBackupDates(const std::string& backupPath) {
    auto dates = engine.EnumerateBackupDates(backupPath);
    std::cout << "Date                  Type          Size\n";
    std::cout << "----------------------------------------------------\n";
    for (const auto& date : dates) {
        printf("%-20s %-12s %s\n", date.date.c_str(), 
               date.type.c_str(), date.size.c_str());
    }
}

void showBackupContents(const std::string& backupPath) {
    auto tree = engine.BuildRestoreTree(backupPath);
    printTree(tree, 0);
}

void printTree(const std::vector<RestoreItem>& items, int indent) {
    for (const auto& item : items) {
        std::string prefix(indent * 2, ' ');
        std::cout << prefix << item.name << " (" << item.type << ")\n";
        if (!item.children.empty()) {
            printTree(item.children, indent + 1);
        }
    }
}

void performRestore(const std::string& backupPath, 
                    const std::vector<std::string>& items,
                    const std::string& dest) {
    auto callback = [](int percent, const std::string& msg) {
        printf("\r[%3d%%] %s", percent, msg.c_str());
        fflush(stdout);
    };
    
    bool success = engine.RestoreWithManifest(backupPath, dest, items, 
                                              true, callback);
    if (success) {
        std::cout << "\n? Restore completed successfully!\n";
    } else {
        std::cerr << "\n? Restore failed: " << engine.GetLastError() << "\n";
    }
}
```

**Interactive Mode**:
Same 3-step flow as TUI but with text-based menus instead of ncurses.

---

### 3. **restore_gui.cpp** (GTK Graphical Interface)
**Status**: ?? NEEDS UPDATE

**Required GTK Widgets**:
```cpp
// Main window with notebook (tabs) or stack (for wizard steps)
GtkWidget* notebook;

// Step 1: Backup Selection Page
GtkWidget* backupPathEntry;
GtkWidget* btnBrowseBackup;
GtkWidget* btnLoadDates;
GtkWidget* listViewDates;  // GtkTreeView with columns: Date, Type, Size

// Step 2: Item Selection Page  
GtkWidget* treeViewItems;  // GtkTreeView with checkboxes
GtkWidget* btnExpandAll;
GtkWidget* btnCollapseAll;
GtkWidget* btnSelectAll;
GtkWidget* btnUnselectAll;

// Step 3: Destination Page
GtkWidget* radioOriginal;  // Restore to original location
GtkWidget* radioNew;       // Restore to new location
GtkWidget* destPathEntry;
GtkWidget* btnBrowseDest;
GtkWidget* chkOverwrite;
GtkWidget* chkPreservePerms;

// Progress dialog
GtkWidget* progressDialog;
GtkWidget* progressBar;
GtkWidget* lblProgressMsg;
```

**Key Functions**:
```cpp
void on_btnLoadDates_clicked(GtkWidget* widget, gpointer data) {
    std::string path = gtk_entry_get_text(GTK_ENTRY(backupPathEntry));
    auto dates = engine->EnumerateBackupDates(path);
    
    GtkListStore* store = GTK_LIST_STORE(
        gtk_tree_view_get_model(GTK_TREE_VIEW(listViewDates)));
    gtk_list_store_clear(store);
    
    for (const auto& date : dates) {
        GtkTreeIter iter;
        gtk_list_store_append(store, &iter);
        gtk_list_store_set(store, &iter,
            0, date.date.c_str(),
            1, date.type.c_str(),
            2, date.size.c_str(),
            3, date.path.c_str(),
            -1);
    }
}

void on_btnNext1_clicked(GtkWidget* widget, gpointer data) {
    // Get selected backup date
    GtkTreeSelection* selection = gtk_tree_view_get_selection(
        GTK_TREE_VIEW(listViewDates));
    
    if (gtk_tree_selection_count_selected_rows(selection) == 0) {
        showError("Please select a backup date");
        return;
    }
    
    // Load contents and switch to step 2
    loadBackupContents();
    gtk_notebook_set_current_page(GTK_NOTEBOOK(notebook), 1);
}

void loadBackupContents() {
    auto tree = engine->BuildRestoreTree(selectedBackupPath);
    
    GtkTreeStore* store = GTK_TREE_STORE(
        gtk_tree_view_get_model(GTK_TREE_VIEW(treeViewItems)));
    gtk_tree_store_clear(store);
    
    for (const auto& item : tree) {
        addTreeItem(store, NULL, item);
    }
}

void addTreeItem(GtkTreeStore* store, GtkTreeIter* parent, 
                 const RestoreItem& item) {
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

void on_btnRestore_clicked(GtkWidget* widget, gpointer data) {
    // Collect checked items
    std::vector<std::string> selectedPaths;
    collectCheckedItems(selectedPaths);
    
    if (selectedPaths.empty()) {
        showError("Please select at least one item to restore");
        return;
    }
    
    // Show progress dialog
    showProgressDialog();
    
    // Perform restore
    std::string dest = gtk_toggle_button_get_active(
        GTK_TOGGLE_BUTTON(radioOriginal)) ? "" : 
        gtk_entry_get_text(GTK_ENTRY(destPathEntry));
    
    auto callback = [](int percent, const char* msg) {
        gtk_progress_bar_set_fraction(
            GTK_PROGRESS_BAR(progressBar), percent / 100.0);
        gtk_label_set_text(GTK_LABEL(lblProgressMsg), msg);
        
        // Process GTK events
        while (gtk_events_pending()) {
            gtk_main_iteration();
        }
    };
    
    bool success = engine->RestoreWithManifest(
        selectedBackupPath, dest, selectedPaths, true, callback);
    
    gtk_widget_destroy(progressDialog);
    
    if (success) {
        showInfo("Restore completed successfully!");
    } else {
        showError("Restore failed: " + engine->GetLastError());
    }
}
```

---

## 4. **restore_engine.cpp** Updates

All three applications depend on `restore_engine.cpp`. Add these new methods:

```cpp
struct BackupDate {
    std::string date;      // "2026-01-30 14:30:00"
    std::string type;      // "Full", "Incremental", "Differential"
    std::string size;      // "2.5 GB"
    std::string path;      // Full path to backup folder
};

struct RestoreItem {
    std::string name;      // Display name
    std::string path;      // Full path
    std::string type;      // "Disk", "Volume", "Folder", "File"
    bool checked;
    std::vector<RestoreItem> children;
};

class RestoreEngine {
public:
    // NEW: Enumerate backup dates in a folder
    std::vector<BackupDate> EnumerateBackupDates(const std::string& backupPath);
    
    // NEW: Build hierarchical tree of backup contents
    std::vector<RestoreItem> BuildRestoreTree(const std::string& backupPath);
    
    // NEW: Restore selected items from manifest
    bool RestoreWithManifest(
        const std::string& backupPath,
        const std::string& destPath,
        const std::vector<std::string>& items,
        bool overwrite,
        std::function<void(int, const std::string&)> callback);
    
    // Existing methods remain unchanged
    std::vector<std::string> ListDisks();
    int MountNTFSPartition(const std::string& device, const std::string& mountPoint);
    std::vector<std::string> ScanForBackups(const std::string& path);
    std::string GetLastError();
};
```

---

## Implementation Priority

1. **? DONE**: restore_tui.cpp (created as restore_tui_new.cpp)
2. **High Priority**: restore_engine.cpp (add new methods)
3. **Medium Priority**: restore_cli.cpp (add new CLI arguments)
4. **Lower Priority**: restore_gui.cpp (full GTK rewrite)

---

## Testing Checklist

- [ ] Compile all three applications
- [ ] Test date enumeration with real backup folders
- [ ] Test tree navigation and selection
- [ ] Test restore to original location
- [ ] Test restore to new location
- [ ] Test progress callbacks
- [ ] Test with Full/Incremental/Differential backups
- [ ] Test error handling (invalid paths, permissions, etc.)

---

## Build Commands

```bash
cd LinuxRestore

# Build TUI version
g++ -o restore_tui restore_tui_new.cpp -lncurses -lstdc++fs -std=c++17

# Build CLI version
g++ -o restore_cli restore_cli.cpp -lstdc++fs -std=c++17

# Build GUI version (requires GTK+ 3.0)
g++ -o restore_gui restore_gui.cpp `pkg-config --cflags --libs gtk+-3.0` -lstdc++fs -std=c++17
```

---

## Notes

- All three applications now share the same restore workflow as Windows
- Users can select specific backup dates (essential for incremental/differential)
- Tree view allows granular selection of what to restore
- Destination mapping enables flexible restore scenarios
- This ensures consistent user experience across Windows and Linux recovery environments
