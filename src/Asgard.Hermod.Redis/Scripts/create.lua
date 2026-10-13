-- 创建值及初始 TTL 使用同一脚本，先检查所有权限；同名键绝不删除或覆盖。
local k = KEYS[1]
local kind,expiry,count = ARGV[1],tonumber(ARGV[2]),tonumber(ARGV[3])
if redis.call('EXISTS',k) == 1 then return 0 end
local commands = {string='SET',hash='HSET',list='RPUSH',set='SADD',zset='ZADD',stream='XADD'}
local command = commands[kind]
if not command or not count or count < 1 or (kind == 'string' and count ~= 1) then return -2 end
local values = {}
if kind == 'stream' then values[1] = '*' end
for i=1,count do
  local p=4+(i-1)*3
  local name,value,score = ARGV[p],ARGV[p+1],ARGV[p+2]
  if kind == 'string' or kind == 'list' then values[#values+1]=value
  elseif kind == 'hash' or kind == 'stream' then values[#values+1]=name; values[#values+1]=value
  elseif kind == 'set' then values[#values+1]=name
  else values[#values+1]=score; values[#values+1]=name end
end
if not redis.acl_check_cmd(command,k,unpack(values)) then return -1 end
if expiry ~= -1 and (not expiry or expiry < 1) then return -2 end
if expiry > 0 and not redis.acl_check_cmd('PEXPIRE',k,expiry) then return -1 end
redis.call(command,k,unpack(values))
if expiry > 0 then redis.call('PEXPIRE',k,expiry) end
return 1
