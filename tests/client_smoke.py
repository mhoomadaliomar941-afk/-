import mcharness, os, time, sys
env = mcharness.load_env()
cdir = os.path.join(mcharness.ROOT, 'run', 'client')
os.makedirs(os.path.join(cdir, 'assets'), exist_ok=True)
args = [env['java8'], '-Xmx2G', '-Djavax.accessibility.assistive_technologies= ', '-Djava.library.path=' + env['client.natives'],
        '-Dfml.ignoreInvalidMinecraftCertificates=true', '-cp', env['client.classpath'],
        'net.minecraft.launchwrapper.Launch', '--tweakClass', 'net.minecraftforge.fml.common.launcher.FMLTweaker',
        '--username', 'Player', '--version', '1.12.2', '--gameDir', cdir, '--assetsDir', os.path.join(cdir, 'assets'),
        '--assetIndex', '1.12', '--accessToken', '0', '--userType', 'legacy', '--uuid', '00000000000000000000000000000001',
        '--width', '854', '--height', '480'] + sys.argv[1:]
os.environ['DISPLAY'] = ':99'
p = mcharness.Proc(args, cdir, 'client', '/tmp/claude-0/client_smoke.log')
try:
    m = p.wait_for(r'hello minecraft|Exception', timeout=400)
    print('->', m.group(0))
    m = p.wait_for(r'Created: \d+x\d+x\d+ textures-atlas|Exception in|Error', timeout=400)
    print('->', m.string[:200])
    time.sleep(20)
finally:
    p.p.terminate(); time.sleep(3); p.p.kill()
print([l for l in p.lines if 'OpenGL' in l or 'LWJGL' in l][:4])
