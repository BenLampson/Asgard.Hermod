-- 在服务器端比较完整基线；没有任何盲写降级。先校验权限再修改，避免脚本中途失败。
local k = KEYS[1]
local op,oldType,newType = ARGV[1],ARGV[2],ARGV[4]
local oldCount,newCount = tonumber(ARGV[3]),tonumber(ARGV[5])
local oldLength = tonumber(ARGV[6])
local function row(i) local p=7+(i-1)*4; return ARGV[p],ARGV[p+1],ARGV[p+2],ARGV[p+3] end
if redis.call('TYPE',k).ok ~= oldType then return 0 end
-- Hash 只校验并修改有变化的字段；其他客户端修改无关字段不会造成盲覆盖。
if oldType == 'hash' and newType == 'hash' and op == 'save' then
  if newCount == 0 then return -2 end
  local old,new = {},{}
  for i=1,oldCount do local name,value=row(i); old[name]=value end
  for i=1,newCount do local name,value=row(oldCount+i); new[name]=value end
  local sets,deletes = {},{}
  for name,value in pairs(old) do
    if new[name] ~= value then
      if redis.call('HGET',k,name) ~= value then return 0 end
      if new[name] == nil then deletes[#deletes+1]=name end
    end
  end
  for name,value in pairs(new) do
    if old[name] ~= value then
      if old[name] == nil and redis.call('HEXISTS',k,name) == 1 then return 0 end
      sets[#sets+1]=name; sets[#sets+1]=value
    end
  end
  if #sets>0 and not redis.acl_check_cmd('HSET',k,unpack(sets)) then return -1 end
  if #deletes>0 and not redis.acl_check_cmd('HDEL',k,unpack(deletes)) then return -1 end
  if #sets>0 then redis.call('HSET',k,unpack(sets)) end
  if #deletes>0 then redis.call('HDEL',k,unpack(deletes)) end
  return 1
end
if oldType == 'string' then
  local _,value = row(1)
  if redis.call('STRLEN',k) ~= #value or redis.call('GET',k) ~= value then return 0 end
elseif oldType == 'hash' or oldType == 'set' or oldType == 'zset' or oldType == 'list' then
  local lengths = {hash='HLEN',set='SCARD',zset='ZCARD',list='LLEN'}
  if redis.call(lengths[oldType],k) ~= oldCount then return 0 end
  for i=1,oldCount do
    local name,value,score = row(i)
    if oldType == 'hash' and redis.call('HGET',k,name) ~= value then return 0 end
    if oldType == 'list' and redis.call('LINDEX',k,i-1) ~= value then return 0 end
    if oldType == 'set' and redis.call('SISMEMBER',k,name) ~= 1 then return 0 end
    if oldType == 'zset' and tonumber(redis.call('ZSCORE',k,name)) ~= tonumber(score) then return 0 end
  end
elseif oldType == 'stream' then
  if redis.call('XLEN',k) ~= oldLength then return 0 end
  local records = redis.call('XRANGE',k,'-','+','COUNT',1001)
  local index = 1
  for i=1,#records do for j=1,#records[i][2],2 do
    local name,value,_,id = row(index)
    if id ~= records[i][1] or name ~= records[i][2][j] or value ~= records[i][2][j+1] then return 0 end
    index=index+1
  end end
  if index-1 ~= oldCount then return 0 end
elseif oldType ~= 'none' then return 0 end
if op == 'delete' then
  if not redis.acl_check_cmd('DEL',k) then return -1 end
  redis.call('DEL',k); return 1
end
if newType ~= 'string' and newCount == 0 then return -2 end
local commands = {string='SET',hash='HSET',list='RPUSH',set='SADD',zset='ZADD',stream='XADD'}
local command = commands[newType]
if not command then return -2 end
local values = {}
if newType == 'stream' then
  if oldType ~= 'none' then return -2 end
  values[1] = '*'
end
for i=1,newCount do
  local name,value,score = row(oldCount+i)
  if newType == 'string' or newType == 'list' then values[#values+1]=value
  elseif newType == 'hash' or newType == 'stream' then values[#values+1]=name; values[#values+1]=value
  elseif newType == 'set' then values[#values+1]=name
  else values[#values+1]=score; values[#values+1]=name end
end
if not redis.acl_check_cmd(command,k,unpack(values)) or not redis.acl_check_cmd('DEL',k) then return -1 end
local expiry = oldType == 'none' and -1 or redis.call('PEXPIRETIME',k)
if expiry > 0 and not redis.acl_check_cmd('PEXPIREAT',k,expiry) then return -1 end
redis.call('DEL',k)
redis.call(command,k,unpack(values))
if expiry > 0 then redis.call('PEXPIREAT',k,expiry) end
return 1
