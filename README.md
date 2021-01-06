# NUS Ripper
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://choosealicense.com/licenses/mit/)

A tool made to download, prepare, extract from, and create a complete [No-Intro](https://no-intro.org/) dat for DSiWare CDN content.

To see the completed dat (which includes no actual files nor copyrighted content of any kind, only references to them), please visit [Dat-o-Matic's **Nintendo DSi (Digital) (CDN)**](https://datomatic.no-intro.org/index.php?page=search&op=datset&s=147&sel_s=147&text=&where=1&button=Search) for the dat and [DSiBrew's **Nintendo CDN Files** page](https://dsibrew.org/wiki/Nintendo_CDN_Files) for information found during the development of this project.

## Please Note
Decryption of content will not work, as that portion of the code depends on a DLL that I am not comfortable passing along. Other than that, the tool works perfectly well for creating a CDN archive, but keep in mind it was originally made to make the creation of a specific dat easier, and is not designed for general use (though in some contexts it will work fine for them).

The code will not compile without the DLL present, but precompiled versions (which you can find without the DLL in the [Releases](https://github.com/zedseven/NusRipper/releases) tab) will run fine for the creation of CDN archives.

To use the tool, you must provide your own list of title IDs and files to download. The tool *does not provide these for you*.
