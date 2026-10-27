# Small helpers for Data/Scripts.rxdata, used by release.py.
#
#   ruby rxscripts.rb empty <Scripts.rxdata>
#       Replace it with an empty script list (so scripts_combine.rb will rebuild it).
#   ruby rxscripts.rb compare <a.rxdata> <b.rxdata>
#       Exit 0 if both hold the same scripts (names and code), else list differences.
#   ruby rxscripts.rb disable <Scripts.rxdata> <section name>
#       In that section, change the first "ENABLED = true" to "ENABLED = false".
#   ruby rxscripts.rb show <Scripts.rxdata> <section name>
#       Print that section's ENABLED line.
require "zlib"

def load(path) = Marshal.load(File.binread(path))
def body(entry) = Zlib::Inflate.inflate(entry[2])
def section(scripts, name)
  found = scripts.select { |s| s[1] == name }
  abort "Found #{found.size} script sections named '#{name}' (expected 1)." if found.size != 1
  found[0]
end

case ARGV[0]
when "empty"
  File.binwrite(ARGV[1], Marshal.dump([]))
when "compare"
  a = load(ARGV[1]).map { |s| [s[1], body(s)] }
  b = load(ARGV[2]).map { |s| [s[1], body(s)] }
  exit 0 if a == b
  names = (a - b).map(&:first) | (b - a).map(&:first)
  puts "Sections that differ: #{names.reject(&:empty?).first(12).join(', ')}"
  exit 1
when "disable"
  scripts = load(ARGV[1])
  s = section(scripts, ARGV[2])
  text = body(s)
  new_text = text.sub(/^(\s*ENABLED\s*=\s*)true\b/) { "#{$1}false" }
  if new_text == text
    abort "No 'ENABLED = true' in #{ARGV[2]}." unless text =~ /^\s*ENABLED\s*=\s*false\b/
  end
  s[2] = Zlib::Deflate.deflate(new_text)
  File.binwrite(ARGV[1], Marshal.dump(scripts))
when "show"
  puts body(section(load(ARGV[1]), ARGV[2])).lines.grep(/^\s*ENABLED\s*=/).first.to_s.strip
else
  abort "Unknown command #{ARGV[0]}"
end
