# Activity Management Quick Start Guide

## ?? How to Use the New Activity Management System

### Opening Activity Management

**Option 1: From Menu**
```
Main Window ? Activity ? Activity Management...
```

**Option 2: Keyboard Shortcut** (if you add one)
```
Alt + A ? A
```

---

## ?? Viewing Job Activities

### Step 1: See All Jobs
When you open Activity Management, you see a list of all backup jobs with their activity summaries:

| Job Name | Total | Last Activity | ? (Success) | ? (Warning) | ? (Error) |
|----------|-------|---------------|-------------|-------------|-----------|
| Daily Backup | 150 | 02/16 15:30 | 145 | 3 | 2 |
| Weekly Backup | 45 | 02/15 08:00 | 45 | 0 | 0 |

### Step 2: View Job Details
**Method 1:** Double-click the job row  
**Method 2:** Click the "View Details" button

This opens the Activity Detail window showing all activities for that job.

---

## ?? Selecting Activities

### Single Selection
Just click on any row.

### Multiple Selection (Range)
1. Click the first activity
2. Hold **Shift**
3. Click the last activity
4. All activities between are selected

### Multiple Selection (Individual)
1. Click the first activity
2. Hold **Ctrl**
3. Click additional activities
4. Each clicked activity is added to selection

### Select All
Right-click ? **Select All**

### Clear Selection
Right-click ? **Clear Selection**

---

## ?? Exporting Activities

### Method 1: Using Buttons
1. Select the activities you want to export
2. Click **"Export to CSV"** or **"Export to Text"**
3. Choose where to save the file
4. Click Save

### Method 2: Using Right-Click Menu
1. Select activities
2. Right-click anywhere in the list
3. Choose **"Export Selected to CSV"** or **"Export Selected to Text"**
4. Choose destination and save

### Export Formats

**CSV (Excel)**
- Opens in Excel, Google Sheets, etc.
- Sortable columns
- Good for analysis
- File extension: `.csv`

**Text (Human-Readable)**
- Opens in Notepad, Word, etc.
- Easy to read
- Good for emails
- File extension: `.txt`

---

## ??? Deleting Activities

### ?? Warning: Deletion is Permanent!

### Method 1: Using Delete Button
1. Select activities to delete
2. Click **"Delete Selected"** button
3. Confirm deletion in dialog
4. Activities are permanently removed

### Method 2: Using Right-Click Menu
1. Select activities
2. Right-click
3. Choose **"Delete Selected"**
4. Confirm deletion

### Tips:
- Review selection before deleting
- Start with a small test selection
- Export before deleting (for backup)
- Cannot undo deletion

---

## ?? Filtering Activities

Use the filter dropdown to show only specific types:

- **All** - Shows everything
- **Info** - Informational messages
- **Success** - Successful operations
- **Warning** - Warnings (non-critical)
- **Error** - Errors (failures)

Your selection is preserved when you change filters!

---

## ?? Common Tasks

### Task: Export Last Week's Backup Activities
1. Open Activity Management
2. Find your backup job
3. Click "View Details"
4. Select all activities (Right-click ? Select All)
5. Export to CSV
6. Save as `backup_week_of_[date].csv`

### Task: Delete Test Backup Logs
1. Open Activity Management
2. Find "Test Backup" job
3. Click "View Details"
4. Ctrl+Click each test entry
5. Right-click ? "Delete Selected"
6. Confirm

### Task: Email Error Report
1. Open Activity Management
2. Find problem job
3. Click "View Details"
4. Filter: "Error"
5. Select All errors
6. Export to Text
7. Attach text file to email

### Task: Review Job Health
1. Open Activity Management
2. Look at the color columns:
   - **Green (?)** - Successes
   - **Orange (?)** - Warnings
   - **Red (?)** - Errors
3. Jobs with many errors need attention
4. Click "View Details" to investigate

### Task: Quarterly Audit Export
1. Open Activity Management
2. Click "View All Activities"
3. Select All (Ctrl+A or Right-click ? Select All)
4. Export to CSV
5. Save as `backup_audit_Q1_2026.csv`
6. Store for compliance

---

## ?? Pro Tips

### Tip 1: Use Filters Before Export
Instead of exporting everything and filtering in Excel:
1. Use the filter dropdown first
2. Filter to only what you need
3. Then export

### Tip 2: Export Before Bulk Delete
Before deleting many activities:
1. Export them first (backup)
2. Review export file
3. Then delete if confirmed

### Tip 3: Check Selection Count
The blue text shows how many items are selected:
```
"5 activities selected"
```
Verify this matches what you expect before export/delete!

### Tip 4: Row Details
Click the ? arrow on the left of any row to expand and see full details:
- Complete message text
- Detailed information
- Backup path
- Validation status

### Tip 5: Keyboard Shortcuts
- **Ctrl+A** - Select All
- **Ctrl+Click** - Add to selection
- **Shift+Click** - Range select
- **Right-Click** - Context menu

---

## ?? Important Notes

### About Deletion
- Deleted activities **CANNOT** be recovered
- Always confirm you're deleting the right items
- Consider exporting before deleting
- Deletion is immediate (no undo)

### About CSV Files
- Open with Excel, Google Sheets, or any spreadsheet
- Comma-separated values
- First row is headers
- UTF-8 encoding (supports special characters)

### About Text Files
- Plain text format
- Opens in any text editor
- Easy to copy/paste
- Good for documentation

### Performance
- System handles 10,000+ activities easily
- Filtering is instant
- Export completes in seconds
- UI stays responsive

---

## ?? Troubleshooting

### Problem: Can't Select Multiple Items
**Solution:** Make sure you're holding Ctrl or Shift while clicking

### Problem: Export Button Disabled
**Solution:** Select at least one activity first

### Problem: "No Selection" Message
**Solution:** Click on activities to select them before exporting/deleting

### Problem: Wrong Items Selected
**Solution:** Right-click ? "Clear Selection" and start over

### Problem: Can't Find Job
**Solution:** Click "Refresh" button to reload the list

### Problem: Export File Won't Open
**Solution:** 
- Check file extension (.csv or .txt)
- Try opening with different program
- Re-export if needed

---

## ?? Getting Help

If you need assistance:
1. Check this guide
2. Look at the status bar (bottom of window)
3. Hover over buttons for tooltips
4. Review error messages carefully
5. Contact support with exported logs

---

## ?? Practice Exercises

### Exercise 1: Basic Export
1. Open Activity Management
2. Choose any job
3. View Details
4. Select 5 activities
5. Export to CSV
6. Open in Excel
7. Verify data looks correct

### Exercise 2: Filtered Delete
1. Open Activity Management
2. View Details for a job
3. Filter to "Info"
4. Select a few info messages
5. Delete them
6. Verify they're gone

### Exercise 3: Range Selection
1. Open Activity Details
2. Click first activity
3. Scroll down
4. Shift+Click activity 10 rows down
5. Verify 10 activities selected
6. Export to Text

---

## ? Quick Reference Card

| Action | Method |
|--------|--------|
| Open Activity Mgmt | Menu ? Activity ? Activity Management |
| View Job Details | Double-click or "View Details" |
| Select Range | Shift+Click |
| Select Multiple | Ctrl+Click |
| Select All | Right-click ? Select All |
| Export CSV | Select ? Export to CSV button |
| Export Text | Select ? Export to Text button |
| Delete | Select ? Delete Selected button |
| Filter | Use dropdown at top |
| Refresh | Click Refresh button |
| Clear Selection | Right-click ? Clear Selection |

---

**Remember:** The Activity Management system is designed to help you monitor, analyze, and manage your backup logs efficiently. Take your time to explore the features and find what works best for your workflow!
