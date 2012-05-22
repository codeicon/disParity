This is the README for disParity Version 0.19

QUICK START INSTRUCTIONS

Installation:

1. Create a new directory somewhere, for example C:\disParity.
2. Extract this .zip file to it.
3. Install the .NET Framework 2.0 on your system.  This is standard under Vista
   and Windows 7, but may not be present under XP.  To download and install 
   .NET Framework 2.0 from Microsoft, go here:

http://www.microsoft.com/downloads/details.aspx?FamilyID=0856EACB-4362-4B0D-8EDD-AAB15C5E04F5&displaylang=en

(One way to determine if you have .NET 2.0 installed is to check whether the 
directory C:\WINDOWS\Microsoft.NET\Framework\v2.0.50727 exists on your system.)

How to create a parity backup:

1. Create a config.txt file in the installation directory specifiying the drives
   and/or folders to protect and where to store the parity data.  See 
   the sample_config.txt file for details and examples.  You can simply edit
   sample_config.txt, but don't forget to rename it to config.txt.

2. Open a command prompt (Start -> All Programs -> Accessories -> Command 
   Prompt), cd to the directory where you extracted the files (e.g. 
   "cd C:\disParity") and type "disparity create" from the command line.
   IMPORTANT!!! Do not change your data files in any way while the snapshot is 
   being created!
3. If you have a lot of data, go to bed, and come back in the morning to see 
   if it has finished yet.

NOTE: disParity now has a simple usage tracking feature.  Please see the 
      release notes for version 0.15 and 0.16 at the bottom of this file for 
      more details.  This feature may cause some software firewall products to
      pop up an alert when disParity is run.  I would appreciate it if, in
      exchange for using this free software, you would allow disParity to
      access the internet, so that I can better track how the software is
      being used.  Thank you!
      
How to update the backup:

1. Open a command prompt, cd to the directory where you installed disParity,
   and type "disparity update".  Any files deleted since the last snapshot or
   update will be removed from the parity.  Any new files added since the last 
   snapshot or update will be added to the parity.  Edited files will also 
   be updated.

How to recover a lost drive:

1. Open a command prompt, cd to the directory where you installed disParity,
   and type "disparity recover [num] [dir]" from the command line.  [num] is
   the drive number to recover.  [dir] is the location where you want the
   recovered files to be written.  This location must have enough space to
   store the recovered data, or else disParity will eventually run out of disk
   space and abort.  An example might be "disparity recover 2 C:\data\" which
   will restore whatever files were on drive #2 to the folder C:\data.
2. Check the log file and make sure that disParity reported "Hash verified" for 
   all of your recovered files.


DISCLAIMERS

This utility is provided as is, with no support, and no guarantees of any 
kind that it will work.  
  
Although unlikely, this utility could somehow malfunction badly, and perhaps 
even cause data loss.  By using this program, you understand that Bad Things 
might happen and you won't blame the author for this.
  
NEVER rely on disParity as your only backup for important data!!!  There are 
many types of possible data loss that disParity cannot protect against.  Some
of them are described further below in this file.  
  
DisParity generates a log file while it runs.  If you report a bug or other
problem I might ask for this file.  Note that the file contains some potentially
embarrassing personal information, in particular the names of all the files it 
processed during the snapshot.  If you don't want me to know what types of
files you have on your server, don't send me this file even I ask for it.  I 
don't personally care in the slightest what files you have and probably won't 
even look at the names, but you might care, so always review the log file 
before sending it to me.


HOW TO REPORT A BUG

Bug reports should be posted under "Bug Reports" in the disParity forums:

http://www.vilett.com/disParity/forum/

You are strongly encouraged to use the forums to report any bugs or ask any
questions about disParity you may have.  That way, all users can benefit from
the answer to your question.  As a last resort, you can also email questions
or bug reports to: rolandv@gmail.com
  
  
ADDITIONAL NOTES AND INSTRUCTIONS

DisParity is intended for use on media servers.  Some assumptions about media 
servers that it makes include:

  * There are multiple drives on the system, each containing lots (i.e.
    many GB) of media data such as music and movies.
  * There is a drive available to be solely dedicated to storing parity data.
    No data to be protected is stored on this drive.  This drive also has
    free space available greater than or equal to the largest data drive.
  * The media files rarely change.  In particular, they won't change while the 
    snapshot is being generated.  If they do, disParity will probably crash.
  * The data generally consists of a relatively small number of large files
    per drive.  "Small number" means a few thousand and "large files" means
    files mostly larger than 1MB.   While it should work on any set of files of
    any size, its performance may degrade the more small files there are.    
          
DisParity can only recover from a single drive failure.  If more than one
drive fails, it won't be able to recover anything.  So don't rely on disParity 
as your only backup of important data.

DisParity also cannot recover from any drive failure (except the parity drive)
that occurs DURING snapshots or updates.  Since snapshot generation is a lengthy 
disk-intensive operation, this is actually not just a remote possibility.  So 
you need to know that you are vulnerable to data loss during creates and 
updates.  Even if you already had a good snapshot, it gets partially overwritten 
during a subsequent update so you can easily lose both the old and the new 
parity data.  Future versions may have enhancements to reduce this risk, but for 
now you should be very aware of it.  So AGAIN: don't rely on disParity as your 
only backup of important data.    

Initial snapshots can take a long time to generate.  Your best bet is to run it
overnight.  A very rough rule of thumb for estimating how long the snapshot will
take is 1 hour for every 500GB of data to be protected.

The data drives to be protected are specified in a very simple file called 
config.txt.  See the included sample_config.txt.  This file is basically 
self-explanatory.  It also specifies where the parity data should be stored.
While I intended the paths to be drive letters (e.g. "D:\") they can actually
be any path (e.g. "D:\Movies\").  They can even be network drives 
(e.g. "\\Media\Photos\") but that will probably slow down disParity quite
a bit.  While you could specify 2 different data paths that are actually on
the same drive, I don't recommend that at all because if that drive were to 
fail, you would lose 2 "drives" simultaneously from disParity's point of view,
and thus not be able to recover anything.


CHANGE HISTORY

0.19

 * Fixed a bug in the free space calculation code that could erroneously
   skip a newly added file during an update.
 * Hidden files and folders are now no longer ignored.  System files and 
   folders are still skipped.    

0.18

 * The default size of the in-memory buffer used for updates has been increased
   from 64MB to 256MB.  This value can also be overridden by specifying 
   "tempram=X" in config.txt, where X is the size in MB.  Note that due to
   limits on per-process memory imposed by Windows, the practical limit for 
   this value is approximately 1500.
 * If the allocation of the in-memory buffer fails during an update, disParity 
   will now fall back automatically to using a temp file.
 * Added a new "undelete" command which restore any files on the given drive
   that have been deleted since the last update.   
    

0.17 BETA

 * This is the first version of disParity released as a beta.  Although I've
   been testing this version for a while now, I decided to release a beta 
   because the changes in this version are significant and may contain new bugs 
   that I haven't been able to detect yet in my own testing.  Beta testers 
   should be aware that a bug in the new code could possibly result in a bad 
   backup.
 * Changed the way the "update" command is implemented so that it should be
   much more resistant to file access problems encountered during execution.
   Modified parity data for added or deleted files is now saved to a
   temporary buffer and not written to the master backup until the entire
   file has been processed.  For small files a in-memory buffer is used,
   but for large files a temporary file is used.  By default this file
   is written to the same directory as the disParity executable, but the
   location can be changed by adding a "temp=" line to config.txt.   
 * Added a new "hashcheck" command which checks the hash code of every
   file on a drive against disParity's previously stored hash code.  This
   can be used to check a drive for corruption.
     
0.16

 * Tweaked usage tracking to fix a case where it could cause disParity to
   stall briefly at startup.
 * Usage tracking now includes an automatic check for updates.  The tracking
   "ping" URL now hits a script which returns the latest disParity release
   number.  If this number is greater than the version being run, disParity
   will print a note at the end of the run indicating that a new version is
   available for download.       

0.15

 * Added primitive anonymous usage tracking.  The first time disParity is
   run it assigns a random ID to itself, and every time disParity is run it
   hits a (currently non-existent) URL on my web server with that ID, the 
   command ("create", "update", etc.) that was invoked, and the current version
   number.  No other data is collected or logged.  This way the error log on my 
   web server can act as a simple way for me to keep track of how many unique 
   users are running disPartiy and which commands they are invoking.  This will
   influence how much time I spend working on improving disParity in the future.
   I would also like to eventually expand this mechanism to add support for 
   automatic notification of new versions, and also auto-upload of crash info.     
 * Fixed parity corruption bug in the edit detection code.  The bug would
   occur if the edited file's length had changed enough to change the number of
   64K parity blocks required to protect the file.
 * Fixed a bug in the handling of zero-length files.  

0.14

 * Fixed crash bug in update code.
 * Added new "stats" command which outputs total file counts and sizes for
   each drive in the array.    

0.13

 * Re-organized this readme file to be more helpful for first-time users.
 * By popular request, disParity now generates a separate log file each time
   it runs.  The log files are named based on the current date and time. 
 * It is now possible to add a new drive to the snapshot without regenerating
   the entire snapshot.  Simply add a new entry to config.txt for the new
   drive and run the "update" command.  However you CANNOT remove a drive
   in the same way.  If you remove a drive from config.txt you must recreate
   the snapshot with the "create" command!
 * There is a new command "list [num]" which dumps a list to the log file of 
   every file name in the snapshot.  If the optional drive [num] argument is
   specified, only files from that drive are listed.
 * Tweaked file name handling a bit to reduce the occurrence of unnecessary
   extra '\' characters. 
   
0.12:

 * Better trapping and reporting of unexpected errors.  Crash message and
   stack trace should now always be logged to the log file.
 * The log file is now appended to rather than erased on each run.  The current 
   time and date and type of command (create, update, etc.) are also logged.
 * disParity will now only check a file's hash code at most once per verify 
   pass, even if the file is involved in multiple failures.
    
0.11:

 * Added new "verify" command.  This command checks all of the parity snapshot
   data against the protected files and reports any mismatches.  Normally
   there should never be any mismatches.  If mismatches are reported, there
   are either bugs in disParity, or files were changed after the verify process
   was begun, or a read error occurred from one of the drives.  Verify can take 
   as long as a "create" command to complete. 

0.10:

 * Added new "test [num]" command which simulates a recovery operation for
   the given drive.  All files for the given drive are recovered from parity 
   and their hash codes are verified, but no data is written to disk.  This is 
   an easy way to verify that the parity data for a drive is correct.  The test 
   command should only be run after a create or update.  If files have been 
   deleted, moved or edited on any drive since the last update, there is a good 
   chance the test will fail.
 * The "recover" command should now be slightly faster.

0.09:

 * Requirement that all paths (parity and data) are absolute is now enforced.
   Relative paths (e.g. "../MyStuff/") are not allowed. 
 * Available disk space on the parity drive is now checked before a create
   or update begins; if not enough space is available, the process will abort.
   This is to hopefully help prevent corruption of the snapshot in the event
   the parity disk should run out of space.
 * Update algorithm is now a little bit smarter about re-using parity space
   made available by deletes. 

0.08:

 * Fixed a bug in the handling of zero-length files
 * The create command now also supports the -v option for verbose logging,
   although very little extra information is currently logged.  

0.07:

 * Added additional error checking throughout the code.
 * All drives listed in the config.txt are checked for access before a
   create or update is allowed.  All drives except for the drive to be 
   recovered are checked for access before a recover is allowed.
 * If the file meta data (filesXX.dat) for any drive cannot be loaded, it
   will now prevent an update or recover.  This will prevent an 
   update if a new path has been added to the config.txt file.  If you want
   to add a new drive to your snapshot, you must recreate the snapshot.  
 * Directory or file access errors during a create will cause a warning to be 
   logged and the file or directory to be skipped, but the process will not 
   abort.
 * All existing parity files in the parity directory are now deleted before a 
   new snapshot is created. 

0.06:

 * Fixed a bug where the update command would keep thinking a file was edited,
   even though the edit had already been detected and processed by a previous
   update.  
 * Added an optional "-v" argument to the update command, which enables
   verbose logging, for debugging purposes.   

0.05:

 * Edits are now supported.  An edited file is treated as a delete of the
   old version of the file followed by an add of the new version. 
 * Made several optimizations that should greatly speed up updates, especially
   adds, at the expense of somewhat increased memory usage.
 * If any data path points to an invalid directory during a create or update,
   the process will abort.  

0.04 

 * File moves and renames are now properly handled without having to
   recalculate parity for the file.  Note that for moves, this only applies to
   files moved to a new location on the same drive.  If a file is moved from
   one drive to another, that is handled as a delete of the old file and an
   add of the new file.
 * Made a small optimization to the file access code for deletes and recovers.
   These operations should be a little bit faster than before (but still quite
   slow.)
  
0.03

 * "Deletes" are now supported.  If you have deleted any files from your
    collection since the last snapshot or update, they will be removed from
    the parity data during the next "update" command.
 * Technically, since adds and deletes are now supported, file moves and 
   renames are now supported.  If you move or rename a file, it will appear to
   disParity as a delete followed by an add.  This will cause disParity to 
   (unnecessarily) recalculate parity for that file, but the snapshot will 
   still be valid.
 * It is not necessary to create your snapshot after upgrading to 0.03
 * More refactoring.  The code is starting to shape up nicely.  
            
0.02

 * "Adds" are now supported.  If you have added new files to your collection
   since the last snapshot, you can quickly update your snapshot to include
   the new files by typing "disparity update".  Edits and deletes are not yet
   supported.      
 * Files and directories marked with the Hidden or System attributes are now
   ignored.
 * Lots of code rewrite/reorganization, which probably means new bugs.
      
0.01  First release
