'json.get' { return $values[0].Get([string]$values[1]) }
'json.has' { return $values[0].Has([string]$values[1]) }
'json.items' { return ,$values[0].Items() }
'json.keys' { return ,$values[0].Keys() }
'json.kind' { return $values[0].Kind }
'json.set' { return $values[0].Set([string]$values[1], $values[2]) }
'json.remove' { return $values[0].Remove([string]$values[1]) }
'json.set_at' { return $values[0].SetAt([int]$values[1], $values[2]) }
'json.remove_at' { return $values[0].RemoveAt([int]$values[1]) }
'json.append' { return $values[0].Append($values[1]) }
