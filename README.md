# AbcPlayer
 A simple .abc file player created in C# .NET
 * Note, this is a WIP (and moreso than that - it's simply a proof-of-concept), and is likely to be buggy at times
 
 ## AbcPlayer (Library)
 - Uses the NAudio framework to produce the required frequencies as sine waves
 - Library is split into a seperate project for ease-of-distribution and use
 
 ## AbcPlayerApp
 - Has CLI support for loading a .abc file from the first given arguement
 - Is able to detect OS file associations for .abc files, and determines whether to open the file with the native editor, or notepad if the program is set as the native editor (accessible by shift-clicking the folder button)
 - Incredibly simple and intuitive design
