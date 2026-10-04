# Time-zone constants

This is the fixed public catalog for `datetime.Zone` constants, referenced by
[the standard-library specification](STDLIB.md#datetime). All constants are read-only.
The catalog covers the 143 Windows IDs enumerated on the development Windows host
on 2026-10-04. It is checked into Rukbat rather than generated on the compiler host.
Windows may add IDs after this catalog; `datetime.zone(id)` accepts additional installed IDs.

Names remove the exact phrase ` Standard Time`, lowercase the remaining ID, and replace
punctuation/space runs with `_`. Hyphens in region names act as separators.
For numeric UTC IDs, `+` becomes `_plus_` and `-` becomes `_minus_`;
leading zeroes are retained. Parenthesized region qualifiers are retained, so
`Pacific Standard Time (Mexico)` is `datetime.pacific_mexico`.
These names have no city-name aliases or abbreviations beyond those in the Windows ID.

Each value uses the specified ID's installed rules on the deployment host,
including daylight saving. The words `Standard Time` in an ID do not disable daylight saving.
Zone values are not fixed numeric offsets. No constant depends on the local-zone setting.
Accessing a constant whose ID is unavailable on that host raises ordinary Error.
Unused constants are not resolved. Windows PowerShell 5.1 remains supported.

Windows documents installed ID discovery via [tzutil /l](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tzutil).
The [TimeZoneInfo.Id documentation](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.id)
describes Windows IDs and their registry source.

| Constant | Windows ID |
|---|---|
| `datetime.afghanistan` | `Afghanistan Standard Time` |
| `datetime.alaskan` | `Alaskan Standard Time` |
| `datetime.alberta` | `Alberta Standard Time` |
| `datetime.aleutian` | `Aleutian Standard Time` |
| `datetime.altai` | `Altai Standard Time` |
| `datetime.arab` | `Arab Standard Time` |
| `datetime.arabian` | `Arabian Standard Time` |
| `datetime.arabic` | `Arabic Standard Time` |
| `datetime.argentina` | `Argentina Standard Time` |
| `datetime.astrakhan` | `Astrakhan Standard Time` |
| `datetime.atlantic` | `Atlantic Standard Time` |
| `datetime.aus_central` | `AUS Central Standard Time` |
| `datetime.aus_central_w` | `Aus Central W. Standard Time` |
| `datetime.aus_eastern` | `AUS Eastern Standard Time` |
| `datetime.azerbaijan` | `Azerbaijan Standard Time` |
| `datetime.azores` | `Azores Standard Time` |
| `datetime.bahia` | `Bahia Standard Time` |
| `datetime.bangladesh` | `Bangladesh Standard Time` |
| `datetime.belarus` | `Belarus Standard Time` |
| `datetime.bougainville` | `Bougainville Standard Time` |
| `datetime.british_columbia` | `British Columbia Standard Time` |
| `datetime.canada_central` | `Canada Central Standard Time` |
| `datetime.cape_verde` | `Cape Verde Standard Time` |
| `datetime.caucasus` | `Caucasus Standard Time` |
| `datetime.cen_australia` | `Cen. Australia Standard Time` |
| `datetime.central` | `Central Standard Time` |
| `datetime.central_america` | `Central America Standard Time` |
| `datetime.central_asia` | `Central Asia Standard Time` |
| `datetime.central_brazilian` | `Central Brazilian Standard Time` |
| `datetime.central_europe` | `Central Europe Standard Time` |
| `datetime.central_european` | `Central European Standard Time` |
| `datetime.central_mexico` | `Central Standard Time (Mexico)` |
| `datetime.central_pacific` | `Central Pacific Standard Time` |
| `datetime.chatham_islands` | `Chatham Islands Standard Time` |
| `datetime.china` | `China Standard Time` |
| `datetime.cuba` | `Cuba Standard Time` |
| `datetime.dateline` | `Dateline Standard Time` |
| `datetime.e_africa` | `E. Africa Standard Time` |
| `datetime.e_australia` | `E. Australia Standard Time` |
| `datetime.e_europe` | `E. Europe Standard Time` |
| `datetime.e_south_america` | `E. South America Standard Time` |
| `datetime.easter_island` | `Easter Island Standard Time` |
| `datetime.eastern` | `Eastern Standard Time` |
| `datetime.eastern_mexico` | `Eastern Standard Time (Mexico)` |
| `datetime.egypt` | `Egypt Standard Time` |
| `datetime.ekaterinburg` | `Ekaterinburg Standard Time` |
| `datetime.fiji` | `Fiji Standard Time` |
| `datetime.fle` | `FLE Standard Time` |
| `datetime.georgian` | `Georgian Standard Time` |
| `datetime.gmt` | `GMT Standard Time` |
| `datetime.greenland` | `Greenland Standard Time` |
| `datetime.greenwich` | `Greenwich Standard Time` |
| `datetime.gtb` | `GTB Standard Time` |
| `datetime.haiti` | `Haiti Standard Time` |
| `datetime.hawaiian` | `Hawaiian Standard Time` |
| `datetime.india` | `India Standard Time` |
| `datetime.iran` | `Iran Standard Time` |
| `datetime.israel` | `Israel Standard Time` |
| `datetime.jordan` | `Jordan Standard Time` |
| `datetime.kaliningrad` | `Kaliningrad Standard Time` |
| `datetime.kamchatka` | `Kamchatka Standard Time` |
| `datetime.korea` | `Korea Standard Time` |
| `datetime.libya` | `Libya Standard Time` |
| `datetime.line_islands` | `Line Islands Standard Time` |
| `datetime.lord_howe` | `Lord Howe Standard Time` |
| `datetime.magadan` | `Magadan Standard Time` |
| `datetime.magallanes` | `Magallanes Standard Time` |
| `datetime.marquesas` | `Marquesas Standard Time` |
| `datetime.mauritius` | `Mauritius Standard Time` |
| `datetime.mid_atlantic` | `Mid-Atlantic Standard Time` |
| `datetime.middle_east` | `Middle East Standard Time` |
| `datetime.montevideo` | `Montevideo Standard Time` |
| `datetime.morocco` | `Morocco Standard Time` |
| `datetime.mountain` | `Mountain Standard Time` |
| `datetime.mountain_mexico` | `Mountain Standard Time (Mexico)` |
| `datetime.myanmar` | `Myanmar Standard Time` |
| `datetime.n_central_asia` | `N. Central Asia Standard Time` |
| `datetime.namibia` | `Namibia Standard Time` |
| `datetime.nepal` | `Nepal Standard Time` |
| `datetime.new_zealand` | `New Zealand Standard Time` |
| `datetime.newfoundland` | `Newfoundland Standard Time` |
| `datetime.norfolk` | `Norfolk Standard Time` |
| `datetime.north_asia` | `North Asia Standard Time` |
| `datetime.north_asia_east` | `North Asia East Standard Time` |
| `datetime.north_korea` | `North Korea Standard Time` |
| `datetime.omsk` | `Omsk Standard Time` |
| `datetime.pacific` | `Pacific Standard Time` |
| `datetime.pacific_mexico` | `Pacific Standard Time (Mexico)` |
| `datetime.pacific_sa` | `Pacific SA Standard Time` |
| `datetime.pakistan` | `Pakistan Standard Time` |
| `datetime.paraguay` | `Paraguay Standard Time` |
| `datetime.qyzylorda` | `Qyzylorda Standard Time` |
| `datetime.romance` | `Romance Standard Time` |
| `datetime.russia_time_zone_10` | `Russia Time Zone 10` |
| `datetime.russia_time_zone_11` | `Russia Time Zone 11` |
| `datetime.russia_time_zone_3` | `Russia Time Zone 3` |
| `datetime.russian` | `Russian Standard Time` |
| `datetime.sa_eastern` | `SA Eastern Standard Time` |
| `datetime.sa_pacific` | `SA Pacific Standard Time` |
| `datetime.sa_western` | `SA Western Standard Time` |
| `datetime.saint_pierre` | `Saint Pierre Standard Time` |
| `datetime.sakhalin` | `Sakhalin Standard Time` |
| `datetime.samoa` | `Samoa Standard Time` |
| `datetime.sao_tome` | `Sao Tome Standard Time` |
| `datetime.saratov` | `Saratov Standard Time` |
| `datetime.se_asia` | `SE Asia Standard Time` |
| `datetime.singapore` | `Singapore Standard Time` |
| `datetime.south_africa` | `South Africa Standard Time` |
| `datetime.south_sudan` | `South Sudan Standard Time` |
| `datetime.sri_lanka` | `Sri Lanka Standard Time` |
| `datetime.sudan` | `Sudan Standard Time` |
| `datetime.syria` | `Syria Standard Time` |
| `datetime.taipei` | `Taipei Standard Time` |
| `datetime.tasmania` | `Tasmania Standard Time` |
| `datetime.tocantins` | `Tocantins Standard Time` |
| `datetime.tokyo` | `Tokyo Standard Time` |
| `datetime.tomsk` | `Tomsk Standard Time` |
| `datetime.tonga` | `Tonga Standard Time` |
| `datetime.transbaikal` | `Transbaikal Standard Time` |
| `datetime.turkey` | `Turkey Standard Time` |
| `datetime.turks_and_caicos` | `Turks And Caicos Standard Time` |
| `datetime.ulaanbaatar` | `Ulaanbaatar Standard Time` |
| `datetime.us_eastern` | `US Eastern Standard Time` |
| `datetime.us_mountain` | `US Mountain Standard Time` |
| `datetime.utc` | `UTC` |
| `datetime.utc_minus_02` | `UTC-02` |
| `datetime.utc_minus_08` | `UTC-08` |
| `datetime.utc_minus_09` | `UTC-09` |
| `datetime.utc_minus_11` | `UTC-11` |
| `datetime.utc_plus_12` | `UTC+12` |
| `datetime.utc_plus_13` | `UTC+13` |
| `datetime.venezuela` | `Venezuela Standard Time` |
| `datetime.vladivostok` | `Vladivostok Standard Time` |
| `datetime.volgograd` | `Volgograd Standard Time` |
| `datetime.w_australia` | `W. Australia Standard Time` |
| `datetime.w_central_africa` | `W. Central Africa Standard Time` |
| `datetime.w_europe` | `W. Europe Standard Time` |
| `datetime.w_mongolia` | `W. Mongolia Standard Time` |
| `datetime.west_asia` | `West Asia Standard Time` |
| `datetime.west_bank` | `West Bank Standard Time` |
| `datetime.west_pacific` | `West Pacific Standard Time` |
| `datetime.yakutsk` | `Yakutsk Standard Time` |
| `datetime.yukon` | `Yukon Standard Time` |
