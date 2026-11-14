package com.waterphysics.client;

import com.google.common.collect.ImmutableList;
import com.waterphysics.block.BlockPhysWater;
import net.minecraft.block.state.IBlockState;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.block.model.BakedQuad;
import net.minecraft.client.renderer.block.model.IBakedModel;
import net.minecraft.client.renderer.block.model.ItemCameraTransforms;
import net.minecraft.client.renderer.block.model.ItemOverrideList;
import net.minecraft.client.renderer.texture.TextureAtlasSprite;
import net.minecraft.client.renderer.vertex.DefaultVertexFormats;
import net.minecraft.client.renderer.vertex.VertexFormat;
import net.minecraft.client.renderer.vertex.VertexFormatElement;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.math.MathHelper;
import net.minecraftforge.client.model.pipeline.UnpackedBakedQuad;
import net.minecraftforge.common.property.IExtendedBlockState;
import net.minecraftforge.fluids.BlockFluidBase;

import javax.annotation.Nullable;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Chunk-baked model of physical water: surface from the 4 corner heights (smooth slopes between
 * neighbouring water levels), flowing texture oriented along the surface gradient, sides, underside, and
 * falling streams rendered as a column whose thickness follows the amount of falling water.
 */
public class WaterBakedModel implements IBakedModel {
    private static final VertexFormat FORMAT = DefaultVertexFormats.ITEM;

    private TextureAtlasSprite still() {
        return Minecraft.getMinecraft().getTextureMapBlocks().getAtlasSprite("minecraft:blocks/water_still");
    }

    private TextureAtlasSprite flow() {
        return Minecraft.getMinecraft().getTextureMapBlocks().getAtlasSprite("minecraft:blocks/water_flow");
    }

    @Override
    public List<BakedQuad> getQuads(@Nullable IBlockState state, @Nullable EnumFacing side, long rand) {
        if (!(state instanceof IExtendedBlockState)) return Collections.emptyList();
        IExtendedBlockState s = (IExtendedBlockState) state;
        Float c0 = s.getValue(BlockFluidBase.LEVEL_CORNERS[0]);
        if (c0 == null) return Collections.emptyList();
        float h00 = c0, h01 = s.getValue(BlockFluidBase.LEVEL_CORNERS[1]), h11 = s.getValue(BlockFluidBase.LEVEL_CORNERS[2]),
                h10 = s.getValue(BlockFluidBase.LEVEL_CORNERS[3]);
        Float flowObj = s.getValue(BlockFluidBase.FLOW_DIRECTION);
        float flowDir = flowObj == null ? -1000f : flowObj;
        Float streamObj = s.getValue(BlockPhysWater.STREAM);
        float stream = streamObj == null ? 0f : streamObj;
        List<BakedQuad> out = new ArrayList<>();
        if (stream > 0f && stream < 0.98f) {
            if (side == null) stream(out, stream, Math.max(Math.max(h00, h01), Math.max(h11, h10)));
            return out;
        }
        TextureAtlasSprite st = still(), fl = flow();
        if (side == EnumFacing.UP) {
            top(out, h00, h01, h11, h10, flowDir, st, fl);
        } else if (side == EnumFacing.DOWN) {
            quad(out, EnumFacing.DOWN, st, new float[][]{
                    {0, 0, 1, st.getMinU(), st.getMaxV()}, {0, 0, 0, st.getMinU(), st.getMinV()},
                    {1, 0, 0, st.getMaxU(), st.getMinV()}, {1, 0, 1, st.getMaxU(), st.getMaxV()}});
        } else if (side != null) {
            side(out, side, h00, h01, h11, h10, fl);
        }
        return out;
    }

    private static void top(List<BakedQuad> out, float h00, float h01, float h11, float h10, float flowDir,
                            TextureAtlasSprite st, TextureAtlasSprite fl) {
        float[] u = new float[4], v = new float[4];
        TextureAtlasSprite tex;
        if (flowDir < -999f) {
            tex = st;
            u[0] = st.getInterpolatedU(0); v[0] = st.getInterpolatedV(0);
            u[1] = u[0]; v[1] = st.getInterpolatedV(16);
            u[2] = st.getInterpolatedU(16); v[2] = v[1];
            u[3] = u[2]; v[3] = v[0];
        } else {
            tex = fl;
            float sn = MathHelper.sin(flowDir) * 0.25f, cs = MathHelper.cos(flowDir) * 0.25f;
            u[0] = fl.getInterpolatedU(8 + (-cs - sn) * 16); v[0] = fl.getInterpolatedV(8 + (-cs + sn) * 16);
            u[1] = fl.getInterpolatedU(8 + (-cs + sn) * 16); v[1] = fl.getInterpolatedV(8 + (cs + sn) * 16);
            u[2] = fl.getInterpolatedU(8 + (cs + sn) * 16); v[2] = fl.getInterpolatedV(8 + (cs - sn) * 16);
            u[3] = fl.getInterpolatedU(8 + (cs - sn) * 16); v[3] = fl.getInterpolatedV(8 + (-cs - sn) * 16);
        }
        float[][] vs = {{0, h00, 0, u[0], v[0]}, {0, h01, 1, u[1], v[1]}, {1, h11, 1, u[2], v[2]}, {1, h10, 0, u[3], v[3]}};
        quad(out, EnumFacing.UP, tex, vs);
        // underside of the surface (visible from under water)
        quad(out, EnumFacing.UP, tex, new float[][]{vs[3], vs[2], vs[1], vs[0]});
    }

    private static void side(List<BakedQuad> out, EnumFacing f, float h00, float h01, float h11, float h10, TextureAtlasSprite fl) {
        float xa, za, xb, zb, ha, hb;
        switch (f) {
            case NORTH: xa = 0; za = 0; xb = 1; zb = 0; ha = h00; hb = h10; break;
            case SOUTH: xa = 1; za = 1; xb = 0; zb = 1; ha = h11; hb = h01; break;
            case WEST: xa = 0; za = 1; xb = 0; zb = 0; ha = h01; hb = h00; break;
            default: xa = 1; za = 0; xb = 1; zb = 1; ha = h10; hb = h11; break;
        }
        float u0 = fl.getInterpolatedU(0), u1 = fl.getInterpolatedU(8);
        float va = fl.getInterpolatedV((1 - ha) * 8), vb = fl.getInterpolatedV((1 - hb) * 8), v0 = fl.getInterpolatedV(8);
        float[][] vs = {{xa, ha, za, u0, va}, {xb, hb, zb, u1, vb}, {xb, 0, zb, u1, v0}, {xa, 0, za, u0, v0}};
        quad(out, f, fl, vs);
        quad(out, f, fl, new float[][]{vs[3], vs[2], vs[1], vs[0]});
    }

    /** a falling stream: vertical column of the given width */
    private void stream(List<BakedQuad> out, float width, float topH) {
        TextureAtlasSprite fl = flow(), st = still();
        float a = 0.5f - width / 2f, b = 0.5f + width / 2f;
        float h = Math.max(0.3f, Math.min(1f, topH));
        float u0 = fl.getInterpolatedU(0), u1 = fl.getInterpolatedU(8 * width);
        float vt = fl.getInterpolatedV(0), vb = fl.getInterpolatedV(8 * h);
        float[][][] faces = {
                {{a, h, a, u0, vt}, {b, h, a, u1, vt}, {b, 0, a, u1, vb}, {a, 0, a, u0, vb}},   // north
                {{b, h, b, u0, vt}, {a, h, b, u1, vt}, {a, 0, b, u1, vb}, {b, 0, b, u0, vb}},   // south
                {{a, h, b, u0, vt}, {a, h, a, u1, vt}, {a, 0, a, u1, vb}, {a, 0, b, u0, vb}},   // west
                {{b, h, a, u0, vt}, {b, h, b, u1, vt}, {b, 0, b, u1, vb}, {b, 0, a, u0, vb}}};  // east
        EnumFacing[] fs = {EnumFacing.NORTH, EnumFacing.SOUTH, EnumFacing.WEST, EnumFacing.EAST};
        for (int i = 0; i < 4; i++) {
            quad(out, fs[i], fl, faces[i]);
            float[][] r = faces[i];
            quad(out, fs[i], fl, new float[][]{r[3], r[2], r[1], r[0]});
        }
        float su0 = st.getInterpolatedU(a * 16), su1 = st.getInterpolatedU(b * 16), sv0 = st.getInterpolatedV(a * 16), sv1 = st.getInterpolatedV(b * 16);
        quad(out, EnumFacing.UP, st, new float[][]{{a, h, a, su0, sv0}, {a, h, b, su0, sv1}, {b, h, b, su1, sv1}, {b, h, a, su1, sv0}});
        quad(out, EnumFacing.DOWN, st, new float[][]{{a, 0, b, su0, sv1}, {a, 0, a, su0, sv0}, {b, 0, a, su1, sv0}, {b, 0, b, su1, sv1}});
    }

    private static void quad(List<BakedQuad> out, EnumFacing face, TextureAtlasSprite tex, float[][] v) {
        UnpackedBakedQuad.Builder b = new UnpackedBakedQuad.Builder(FORMAT);
        b.setQuadOrientation(face);
        b.setTexture(tex);
        b.setQuadTint(0);
        b.setApplyDiffuseLighting(true);
        // normal from the vertex order (works for sloped surfaces and back faces)
        float ax = v[1][0] - v[0][0], ay = v[1][1] - v[0][1], az = v[1][2] - v[0][2];
        float bx = v[2][0] - v[0][0], by = v[2][1] - v[0][1], bz = v[2][2] - v[0][2];
        float nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
        float len = (float) Math.sqrt(nx * nx + ny * ny + nz * nz);
        if (len > 1e-6f) {
            nx /= len;
            ny /= len;
            nz /= len;
        } else {
            nx = face.getFrontOffsetX();
            ny = face.getFrontOffsetY();
            nz = face.getFrontOffsetZ();
        }
        for (float[] vert : v) {
            for (int e = 0; e < FORMAT.getElementCount(); e++) {
                VertexFormatElement el = FORMAT.getElement(e);
                switch (el.getUsage()) {
                    case POSITION: b.put(e, vert[0], vert[1], vert[2], 1f); break;
                    case COLOR: b.put(e, 1f, 1f, 1f, 1f); break;
                    case UV:
                        if (el.getIndex() == 0) b.put(e, vert[3], vert[4], 0f, 1f);
                        else b.put(e);
                        break;
                    case NORMAL: b.put(e, nx, ny, nz, 0f); break;
                    default: b.put(e); break;
                }
            }
        }
        out.add(b.build());
    }

    @Override
    public boolean isAmbientOcclusion() {
        return false;
    }

    @Override
    public boolean isGui3d() {
        return false;
    }

    @Override
    public boolean isBuiltInRenderer() {
        return false;
    }

    @Override
    public TextureAtlasSprite getParticleTexture() {
        return still();
    }

    @Override
    public ItemCameraTransforms getItemCameraTransforms() {
        return ItemCameraTransforms.DEFAULT;
    }

    @Override
    public ItemOverrideList getOverrides() {
        return ItemOverrideList.NONE;
    }

    static List<BakedQuad> none() {
        return ImmutableList.of();
    }
}
