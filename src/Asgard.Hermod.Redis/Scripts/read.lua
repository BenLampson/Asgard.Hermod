-- 原子、有界的读取。编辑基线必须完整；分页结果永不作为完整基线。
local k, mode = KEYS[1], ARGV[1]
local t = redis.call('TYPE', k).ok
local limit, byteLimit = tonumber(ARGV[4]), tonumber(ARGV[5])
local n, rows, cursor, offset, more = 0, {}, '0', tonumber(ARGV[3]), false
if t == 'none' then return {t, 0, -2, 1, '0', 0, 0, rows} end
if t == 'string' then n = redis.call('STRLEN',k)
elseif t == 'hash' then n = redis.call('HLEN',k)
elseif t == 'list' then n = redis.call('LLEN',k)
elseif t == 'set' then n = redis.call('SCARD',k)
elseif t == 'zset' then n = redis.call('ZCARD',k)
elseif t == 'stream' then n = redis.call('XLEN',k)
else return {t,0,redis.call('PTTL',k),0,'0',0,0,rows} end
local full = mode == 'full' and (t == 'string' or n <= limit)
local size = 0
local function add(name,value,score,id)
  size = size + #name + #value + #score + #id
  if size <= byteLimit then rows[#rows+1] = {name,value,score,id} else full = false end
end
if t == 'string' then
  add('',redis.call('GETRANGE',k,0,byteLimit-1),'','')
  full = n <= byteLimit
elseif t == 'hash' then
  local pairs
  if full then pairs = redis.call('HGETALL',k) else
    local page = redis.call('HSCAN',k,ARGV[2],'COUNT',100)
    cursor,pairs = page[1],page[2]; more = cursor ~= '0'
  end
  for i=1,#pairs,2 do add(pairs[i],pairs[i+1],'','') end
elseif t == 'list' then
  if full then offset = 0 end
  local values = redis.call('LRANGE',k,offset,offset+(full and limit or 100)-1)
  for i=1,#values do add(tostring(offset+i-1),values[i],'','') end
  offset = offset + #values; more = offset < n
elseif t == 'set' then
  local values
  if full then values = redis.call('SMEMBERS',k) else
    local page = redis.call('SSCAN',k,ARGV[2],'COUNT',100)
    cursor,values = page[1],page[2]; more = cursor ~= '0'
  end
  for i=1,#values do add(values[i],'','','') end
elseif t == 'zset' then
  local values
  if full then values = redis.call('ZRANGE',k,0,-1,'WITHSCORES') else
    local page = redis.call('ZSCAN',k,ARGV[2],'COUNT',100)
    cursor,values = page[1],page[2]; more = cursor ~= '0'
  end
  for i=1,#values,2 do add(values[i],'',values[i+1],'') end
elseif t == 'stream' then
  local start = full and '-' or (ARGV[2] == '0' and '-' or '('..ARGV[2])
  local records = redis.call('XRANGE',k,start,'+','COUNT',full and limit or 100)
  for i=1,#records do
    local record = records[i]
    for j=1,#record[2],2 do add(record[2][j],record[2][j+1],'',record[1]) end
    cursor = record[1]
  end
  offset = (full and 0 or offset) + #records; more = offset < n
end
return {t,n,redis.call('PTTL',k),full and 1 or 0,cursor,offset,more and 1 or 0,rows,(size>byteLimit or (t=='string' and n>byteLimit)) and 1 or 0}
