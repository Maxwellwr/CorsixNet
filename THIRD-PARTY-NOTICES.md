# Third-party notices

CorsixNet contains code ported from the projects below. Each file with ported
code names its origin in a header comment.

## CorsixTH

Source: https://github.com/CorsixTH/CorsixTH

Ported: sprite chunk decoding (`ChunkDecoder.cs`, from `CorsixTH/Src/th_gfx.cpp`),
the font code page table (`Cp437.cs`, from `CorsixTH/Src/cp437_table.h`),
the map block table and tile rules (`MapFile.cs`, `LevelMapLoader.cs`, `LevelMap.cs`,
from `CorsixTH/Src/th_map.cpp`), the map drawing order (`MapRenderer.cs`),
the pathfinder (`Pathfinder.cs`, from `CorsixTH/Src/th_pathfind.cpp`), animation
loading and drawing (`AnimationSet.cs`, `AnimationRenderer.cs`, from `CorsixTH/Src/th_gfx.cpp`),
entrance doors (`EntranceDoor.cs`, `EntranceDoorDrawing.cs`, from
`CorsixTH/Lua/objects/doors/entrance_*_door.lua`), map object placement and parcel
ownership (`Simulation.cs`, `LevelMapLoader.cs`, from `CorsixTH/Lua/world.lua` and
`CorsixTH/Lua/map.lua`), object footprints and placement rules (`ObjectPlacer.cs`, `BaseGameContent.cs`, `ObjectDrawing.cs`, from
`CorsixTH/Lua/objects/*.lua`, `CorsixTH/Lua/entities/object.lua` and `CorsixTH/Lua/dialogs/place_objects.lua`),
room building rules, windows, room removal, blueprints and wall sets (`RoomPlan.cs`, `Room.cs`, `BuildRoomTool.cs`, `RoomBlueprint.cs`, `BaseGameContent.cs`, `LevelMap.cs`, from
`CorsixTH/Lua/dialogs/edit_room.lua`, `CorsixTH/Lua/rooms/*.lua`, `CorsixTH/Lua/walls/*.lua`,
`CorsixTH/Src/th_lua_map.cpp` and `CorsixTH/Src/th_map.cpp`), walk timing and animation numbers (`Humanoid.cs`, `HumanoidDrawing.cs`, from
`CorsixTH/Lua/humanoid_actions/walk.lua` and `CorsixTH/Lua/entities/humanoid.lua`), and
the RNC decompressor modifications (`Rnc.cs`).

```text
Copyright (C) 2009-2016 Peter "Corsix" Cawley
Copyright (C) 2009-2016 Edvin "Lego3" Linge,
Copyright (C) 2009-2013 Manuel "Roujin" Wolf
Copyright (C) 2009-2012, 2014 Nicolas "MeV" Elie
Copyright (C) 2009 Haico "brainball" van Roeden
Copyright (C) 2009-2012 Darrel "redneon" Blake
Copyright (C) 2009 Manuel König
Copyright (C) 2010 M.Chalon
Copyright (C) 2010 Sjors Gielen
Copyright (C) 2010 Sam Wong
Copyright (C) 2010 Edwin "Dutchy" Smulders
Copyright (C) 2010 Miika-Petteri Matikainen
Copyright (C) 2010-2016 Erlend Mongstad
Copyright (C) 2010-2011 Ole "Froksen" Frandsen
Copyright (C) 2010 Robin "RobseRob" Madsen
Copyright (C) 2010 RAS
Copyright (C) 2010 Jukka Kauppinen
Copyright (C) 2010 "Fabiomsouto"
Copyright (C) 2010 Justin Pasher
Copyright (C) 2010 Wendell Misiedjan
Copyright (C) 2011 David "ChronosHD" Horváth
Copyright (C) 2011 Adam Kirk
Copyright (C) 2011 Ted "IntelOrca" John
Copyright (C) 2011 Chris James
Copyright (C) 2011 John Pirie
Copyright (C) 2011-2015 Mark Lawlor
Copyright (C) 2011-2016, 2020 William "Sadger" Gatens
Copyright (C) 2011-2018 Ola "Grimdoc" Skogrand
Copyright (C) 2011-2015 Jørgen P. Tjernø
Copyright (C) 2011-2012 Zbyněk "teh dude :D" Schwarz
Copyright (C) 2011 Rene "Kleeze" Bergfort
Copyright (C) 2011-2012 Sérgio "Get_It" Ribeiro
Copyright (C) 2011 Sergei Larionov
Copyright (C) 2011 Alexey Pinyagin
Copyright (C) 2011-2019 Giordano "Viandante" Celeghin
Copyright (C) 2012-2026 Stephen "TheCycoONE" Baker
Copyright (C) 2012 Alex Ghiran
Copyright (C) 2012-2013, 2021 Luis "driverpt" Duarte
Copyright (C) 2012-2013 Ryan Hugh Dean
Copyright (C) 2012-2020 Henrique Poyatos
Copyright (C) 2012 lwglwsss
Copyright (C) 2013 Koanxd aka Snowblind
Copyright (C) 2013 Chris "ChrizmanTV" Wilkinson
Copyright (C) 2013-2014, 2024 Alan Woolley
Copyright (C) 2013 YoungSeok Yoon
Copyright (C) 2013-2015 Maarten Peters
Copyright (C) 2013 nullstein
Copyright (C) 2013 BuTcHeR (extragry.pl)
Copyright (C) 2013-2026 Albert "Alberth" Hofkamp
Copyright (C) 2013, 2016-2017, 2025 Antoine Lemaire
Copyright (C) 2014-2015 Joseph "J-Shep" Sheppard
Copyright (C) 2014 Phillipp Röll
Copyright (C) 2014 Leonardo "LeonardoGamer" Malaman
Copyright (C) 2015 Víctor "mccunyao" González
Copyright (C) 2015 Josh Keegan
Copyright (C) 2016-2026 Toby Lane
Copyright (C) 2016-2020 Phil Morrell
Copyright (C) 2017 Arne Zelasko
Copyright (C) 2017 Víctor "IlDucci"
Copyright (C) 2017 "wolfy1339"
Copyright (C) 2017 Fang Xianfu
Copyright (C) 2017 Steven Price
Copyright (C) 2017 Radomir Wojtera
Copyright (C) 2017-2019 David Fairbrother
Copyright (C) 2017-2024 Altieres Lima da Silva
Copyright (C) 2017-2025 Justin "mugmuggy" Mugford
Copyright (C) 2018 Pavel Schoffer
Copyright (C) 2018 Rick "Feanathiel" Megens
Copyright (C) 2018 Anton Shestakov
Copyright (C) 2018 Thilo Ettelt
Copyright (C) 2018 "red031000"
Copyright (C) 2018-2019 James "leiget" Russell
Copyright (C) 2019 Artem Polishchuk
Copyright (C) 2020-2026 "lewri"
Copyright (C) 2020-2021 "yangfl"
Copyright (C) 2020 Jeremy Tellaa
Copyright (C) 2020 Maxim Krivchikov
Copyright (C) 2020 Dimitrios Iliadis
Copyright (C) 2021 Zany XDev
Copyright (C) 2021 Romain "rmnvgr" Vigier
Copyright (C) 2021-2024 Robert "flackr" Flack
Copyright (C) 2021 Alexandre Lavoie
Copyright (C) 2021, 2025-2026 Jan Sako
Copyright (C) 2022 Colin Matthews
Copyright (C) 2022 Frazer Smith
Copyright (C) 2022 "RetroGamerFan"
Copyright (C) 2022 "tomracer"
Copyright (C) 2022 "TotalCaesar659"
Copyright (C) 2022 Manuel K
Copyright (C) 2022-2023 lokpro
Copyright (C) 2022 Raven "Edgewurth" Penfold
Copyright (C) 2022 "SilPho"
Copyright (C) 2022-2026 Matthew "Matroftt"
Copyright (C) 2023 "dfghjkjhgr"
Copyright (C) 2023 Bob "bobidle" Idle
Copyright (C) 2023 Michael "RednibCoding" Binder
Copyright (C) 2023 Nathan "nMustaki" Mustaki
Copyright (C) 2023-2026 Damian "ShiroAka"
Copyright (C) 2024 Peppe "Inkub0" Malara
Copyright (C) 2024 Sandow "Sanndow"
Copyright (C) 2024 Tomás Domínguez "tombolano" Bolaño
Copyright (C) 2024 Ikko E. "eltociear" Ashimine
Copryight (C) 2024 Anthony Dillon
Copyright (C) 2024 SebastianoPistore
Copryight (C) 2024 jetenergy
Copyright (C) 2024 Yurii "JurecStrongman"
Copyright (C) 2024 ShimaKen
Copyright (C) 2024-2026 "MandrakeHorse"
Copyright (C) 2024-2026 Artem Argus "ARGAMX" Gusakov
Copyright (C) 2025 Felipe Muñoz Mazur
Copyright (C) 2025 "pizzadepollo"
Copyright (C) 2025 "legluodunet"
Copyright (C) 2025 "dodero4"
Copyright (C) 2025 "PierreRic"
Copyright (C) 2025 "jur4jmiklo5"
Copyright (C) 2025 "goodusername123"
Copyright (C) 2025 Aarón Rosa "sraaronrock" Díaz
Copyright (C) 2025-2026 Quentin Marcassin "marcassin92" Sanglier
Copyright (C) 2025-2026 "Fraggenard"
Copyright (C) 2026 "ManoloMC002"
Copyright (C) 2026 Arya "algojogacor" Rizky
Copyright (C) 2026 "saya-7"
Copyright (C) 2026 "Ouaz"
et al.

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## MMPX

Source: https://casual-effects.com/research/McGuire2021PixelArt/index.html
(reference implementation, as published in the C99 port https://github.com/ITotalJustice/mmpx).

Ported: `PixelArtScaler.cs`. The port bounds-checks the image edges, unlike the C99 port, whose
`clamp` compares `min > max` instead of `v > max`.

```text
Copyright 2020 Morgan McGuire & Mara Gagiu.
Available under the MIT license.

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## RNC decompressor

Source: CorsixTH `libs/rnc/rnc.cpp`, originally from
http://www.yoda.arachsys.com/dk/utils.html.

Ported: `Rnc.cs`.

```text
Copyright (c) 1997 Simon Tatham

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
