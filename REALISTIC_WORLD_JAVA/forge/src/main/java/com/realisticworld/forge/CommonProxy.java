package com.realisticworld.forge;

import com.realisticworld.ui.Form;

import java.util.List;

/** Dedicated server side: there is no screen, so client actions do nothing. */
public class CommonProxy {
    public void preInit() {}

    public void runOnClient(Runnable r) {}

    public void showForm(Form form) {}

    public void setHud(List<String> lines) {}

    public void setFog(String kind) {}
}
