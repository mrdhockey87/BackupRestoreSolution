# Backup Retention - Quick User Guide

## Overview

The backup retention feature allows you to keep multiple full backup versions automatically. The system safely manages backup rotation, ensuring you never lose your last good backup.

## How to Configure

1. **Open Backup Configuration**
   - Create new backup job OR edit existing job
   - Look for "Full Backup Retention" section in Settings

2. **Set Retention Count**
   - Default: **1** (keeps only latest backup)
   - Range: **1 to 999** (any positive number)
   - Enter desired number of backups to keep

3. **Save Job**
   - Click "Save Job"
   - Retention policy is now active

## What Happens During Backup

### With Retention = 1 (Default)
```
Old: ServerBackup_20260214_100000
  ?
New: ServerBackup_20260214_140000
  ?
Result: Only latest backup kept (old one deleted after verification)
```

### With Retention = 3 (Multiple Versions)
```
Existing Backups:
- ServerBackup_20260212_080000 (oldest)
- ServerBackup_20260213_080000
- ServerBackup_20260214_080000 (newest)

After New Backup:
- ServerBackup_20260213_080000
- ServerBackup_20260214_080000
- ServerBackup_20260215_080000 (new)

Note: Oldest backup (Feb 12) automatically deleted
```

## Safety Features

### ? Your Data is Always Safe

**Before Backup Starts:**
- Existing backup is renamed (not deleted)
- Original backup stays safe during entire process

**During Backup:**
- New backup created alongside renamed backup
- If backup fails, original is restored
- No data loss possible

**After Backup:**
- New backup is verified (if verification enabled)
- Only after successful verification is old backup deleted
- If verification fails, original backup is restored

### ? Automatic Rollback

If anything goes wrong:
1. Failed backup is automatically deleted
2. Original backup is restored
3. You're left with your last working backup

## Common Scenarios

### Daily Backups - Keep Last Week
```
Retention: 7
Result: Last 7 days of backups always available
Oldest backup: Automatically deleted on 8th backup
```

### Weekly Backups - Keep Last Month
```
Retention: 4
Result: Last 4 weeks of backups available
Oldest backup: Automatically deleted on 5th backup
```

### Monthly Backups - Keep Last Year
```
Retention: 12
Result: Last 12 months of backups available
Oldest backup: Automatically deleted on 13th backup
```

### Critical Data - Keep Many Versions
```
Retention: 30
Result: 30 most recent backups available
Perfect for: Important servers, databases, critical files
```

## Storage Planning

Calculate storage needs:

```
Formula: Backup Size × Retention Count = Total Storage

Examples:
- 10GB backup × 7 retention = 70GB needed
- 50GB backup × 7 retention = 350GB needed
- 100GB backup × 30 retention = 3TB needed

Tip: Add 20% buffer for safety
```

## Best Practices

### 1. Match Retention to Schedule

| Schedule | Recommended Retention | Reason |
|----------|----------------------|---------|
| Hourly | 24 | Last 24 hours |
| Daily | 7 | Last week |
| Weekly | 4-8 | Last month(s) |
| Monthly | 12 | Last year |

### 2. Consider Your Disk Space

- Check available storage
- Calculate total needs (see Storage Planning)
- Leave 20% free space buffer
- Monitor disk usage regularly

### 3. Enable Verification

Always enable "Verify backup after completion":
- Ensures backup integrity
- Catches corruption immediately
- Prevents deleting good backup for bad one

### 4. Test Restores

Periodically test restoring from older backups:
- Verify retention is working
- Practice restore procedures
- Ensure backups are usable

## Troubleshooting

### Problem: Running Out of Disk Space

**Solution 1: Reduce Retention**
- Lower retention count
- Keep fewer backups
- More frequent cleanup

**Solution 2: Expand Storage**
- Add more disk space
- Move to larger drive
- Use network storage

**Solution 3: Compress Backups**
- Enable "Compress backup data"
- Reduces backup size by 30-70%
- Slower but saves space

### Problem: Don't See Old Backups

**Possible Causes:**
1. Retention set to 1 (only keeps latest)
2. Cleanup already ran (exceeded retention count)
3. Looking in wrong directory

**Check:**
- Open job configuration
- Verify retention count > 1
- Check backup destination folder
- Look for timestamped folder names

### Problem: Backup Failed, Lost Old Backup

**This Should Never Happen!**

The system automatically:
1. Renames old backup before starting
2. Restores it if new backup fails
3. Never deletes until verification passes

If this occurred:
1. Check Activity log for details
2. Look for folders with `_PENDING_` suffix
3. Manual restore may be possible
4. Contact support if needed

## Activity Log Messages

Normal operation shows:

```
Starting backup job: ServerBackup
Renamed existing backup for safety: ServerBackup_..._PENDING_...
Backing up volume: C:\
Backup completed: 100%
Verifying backup...
Backup verification successful!
Deleting old backup: ServerBackup_..._PENDING_...
Old backup deleted successfully
Retention policy: Keeping 3 backup(s) (limit: 3)
Backup job completed successfully
```

## FAQ

**Q: Does retention work for incremental/differential backups?**
A: No, retention only applies to Full backup type. Incremental/differential have their own chain management.

**Q: Can I change retention after creating job?**
A: Yes! Edit job, change retention count, save. New policy takes effect immediately.

**Q: What if I set retention to 1 after having 10 backups?**
A: Next backup will keep only the new one. Others deleted during cleanup.

**Q: Are old backups compressed before deletion?**
A: No, they're deleted directly. Future version may add archiving.

**Q: Can I have different retention for different jobs?**
A: Yes! Each job has independent retention policy.

**Q: How long does cleanup take?**
A: Very quick - less than 1 second per deleted backup.

**Q: What happens if verification fails?**
A: New backup is deleted, old backup is restored. You never lose data.

**Q: Can I manually delete old backups?**
A: Yes, but not recommended. Let system manage automatically.

**Q: Where are backups stored?**
A: In destination folder specified in job configuration. Each backup is a timestamped subdirectory.

## Quick Reference

| Setting | Effect | Use When |
|---------|--------|----------|
| Retention = 1 | Keep only latest | Limited disk space |
| Retention = 7 | Keep last week | Daily backups |
| Retention = 30 | Keep last month | Critical data |
| Retention = 365 | Keep last year | Long-term archive |

## Need Help?

1. Check Activity log for error messages
2. Review job configuration
3. Verify disk space available
4. Consult VERSION_5.13.3.15_BACKUP_RETENTION.md for technical details
5. Contact support with Activity log

---

**Remember:** The system is designed to protect your data. If in doubt, test with a small non-critical backup job first!
